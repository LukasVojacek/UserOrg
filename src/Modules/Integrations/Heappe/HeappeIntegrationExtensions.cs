using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Threading.Channels;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Features.BackgroundSync;
using Heappe.Features.CommandTemplateManagement;
using Heappe.Features.CommandTemplateSynchronization;
using Heappe.Features.Connection;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.InitializationProjectWithClusters;
using Heappe.Features.KillSwitch;
using Heappe.Features.LocationPartitionsSynchronization;
using Heappe.Features.Nexus;
using Heappe.Features.ResourceValidityAudit;
using Heappe.Features.VersionResolver;
using Heappe.Helpers;
using Heappe.Persistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TokenDelegation;
using TokenDelegation.Features.Strategies;
using TokenDelegation.Models;

using UserOrg.Shared.Abstractions.Interfaces;
using UserOrg.Shared.Extensions;

namespace Heappe;

/// <summary>
/// Provides extension methods and constants for configuring HEAppE integration services and persistence within an
/// application.
/// </summary>
/// <remarks>This class contains methods for registering HEAppE-related services, HTTP clients, and request
/// handlers with the application's dependency injection container. It is intended to be used during application startup
/// to enable HEAppE integration features.</remarks>
public static class HeappeIntegrationExtensions
{
    /// <summary>
    /// Name of the HEAppE client used for HttpClient registration and resolution.
    /// </summary>
    internal const string HeappeClientName = "HeappeClient";

    /// <summary>
    /// Dedicated client for the version/health probe. Separate from <see cref="HeappeClientName"/> so the probe
    /// does not share its long data-call timeouts or its circuit breaker — a slow/dead instance's probes must
    /// not trip the breaker for healthy instances' data calls.
    /// </summary>
    internal const string HeappeHealthClientName = "HeappeHealthClient";

    /// <summary>Client used only for the fail-fast liveness probe against <c>heappe/Health</c>.</summary>
    internal const string HeappeProbeClientName = "HeappeProbeClient";

    /// <summary>Liveness probe timeout (seconds). A dead host fails this fast instead of dragging the whole request.</summary>
    internal const int HeappeProbeTimeout = 10;

    /// <summary>
    /// Configuration key for the token exchange feature flag in the FeatureManagement section.
    /// </summary>
    internal const string TokenExchangeFlagConfigurationKey = $"FeatureManagement:{UserOrg.Shared.FeatureManagement.FeatureFlags.TokenExchangeEnabled}";

    /// <summary>
    /// Configures HEAppE integration services and persistence for the application.
    /// </summary>
    /// <remarks>This method registers all required HEAppE integration services and persistence components
    /// with the application's dependency injection container. Call this method during application startup to enable
    /// HEAppE-related functionality.</remarks>
    /// <param name="hostBuilder">The host application builder to configure with HEAppE integration services. Cannot be null.</param>
    /// <returns>The same <see cref="IHostApplicationBuilder"/> instance so that additional configuration can be chained.</returns>
    public static IHostApplicationBuilder AddHEAppEIntegration(this IHostApplicationBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        hostBuilder.AddHeappeIntegrationPersistence();
        hostBuilder.AddBackgroundChannelWorker();
        hostBuilder.Services.AddHostedService<HeappeDbInitializerService>();
        hostBuilder.Services.AddHostedService<HeappeResourceAuditWorker>();

        hostBuilder.Services.AddServices(hostBuilder.Configuration);
        ConfigureTokenExchangeAndNexus(hostBuilder);

        return hostBuilder;
    }

    private static bool IsFlagOn(string? value) =>
        value?.Trim() is { } text && (text == "1" || (bool.TryParse(text, out var flag) && flag));

    private static void ConfigureTokenExchangeAndNexus(IHostApplicationBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);

        var tokenExchangeEnabled = IsFlagOn(hostBuilder.Configuration[TokenExchangeFlagConfigurationKey]);
        var tokenExchangeSection = hostBuilder.Configuration.GetSection(TokenExchangeOptions.SectionName);
        hostBuilder.Services.AddOptionsWithValidateOnStart<TokenExchangeOptions>().Bind(tokenExchangeSection);
        hostBuilder.Services.AddSingleton<IValidateOptions<TokenExchangeOptions>>(new TokenExchangeOptionsValidator(tokenExchangeEnabled));

        var nexusApiSection = hostBuilder.Configuration.GetSection(NexusOptions.SectionName);
        var x = hostBuilder.Services.AddOptionsWithValidateOnStart<NexusOptions>().Bind(nexusApiSection);
        hostBuilder.Services.AddSingleton<IValidateOptions<NexusOptions>>(new NexusOptionsValidator(tokenExchangeEnabled));
        if (tokenExchangeEnabled)
        {
            hostBuilder.Logging.AddFilter("TokenDelegation", LogLevel.Error);
            hostBuilder.Services.AddNexusRegistry();
            hostBuilder.Services.AddTokenExchange(hostBuilder.Configuration);
        }
        else
        {
            hostBuilder.Services.AddScoped<IHeappeInstanceDirectory, ProxyOnlyHeappeInstanceDirectory>();
            hostBuilder.Services.AddScoped<IHeappeTokenExchanger, PassThroughTokenExchanger>();
        }
    }

    /// <summary>
    /// Registers the NexusApi registry side with the flag on: the singleton service-account token provider, the
    /// registry client (which sends that token), both at <c>TokenExchange:Nexus:BaseUrl</c>, and the directory that
    /// turns a HEAppE identifier into its proxy route.
    /// </summary>
    private static void AddNexusRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<NexusServiceTokenProvider>();
        services.AddTransient<NexusServiceTokenHandler>();
        services.AddHttpClient(NexusServiceTokenProvider.ClientName, UseNexusBaseUrl);
        services.AddHttpClient<NexusRegistryClient>(UseNexusBaseUrl)
            .AddHttpMessageHandler<NexusServiceTokenHandler>();
        services.AddScoped<IHeappeInstanceDirectory, HeappeInstanceDirectory>();
    }

    private static void UseNexusBaseUrl(IServiceProvider services, HttpClient client) =>
        client.BaseAddress = services.GetRequiredService<IOptions<TokenExchangeOptions>>().Value.Nexus.BaseUrl;

    /// <summary>
    /// Registers the per-instance token exchange (ADR-0118) with the flag on: the memory cache, the scoped exchanger
    /// and TokenDelegation. The delegation client and its fail-fast pipeline exist only when the options are
    /// configured; otherwise a hosted service logs at startup which settings are missing.
    /// </summary>
    private static void AddTokenExchange(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IHeappeTokenExchanger, HeappeTokenExchanger>();

        var options = configuration.GetSection(TokenExchangeOptions.SectionName).Get<TokenExchangeOptions>();
        if (options is not { ClientId: not null, ClientSecret: not null, TokenAuthorityEndpoint: not null })
        {
            return;
        }

        services.AddTokenDelegation(delegation =>
        {
            delegation.AddClient(
                    TokenExchangeOptions.DelegationClientName,
                    new ClientMetadata(
                        options.ClientId!,
                        options.ClientSecret!,
                        new Audience(options.ClientId!, options.TokenAuthorityEndpoint),
                        typeof(OidcOnBehalfOfStrategy))
                    {
                        TokenEndpoint = options.TokenAuthorityEndpoint,
                    })
                .ConfigureHttpClient(http =>
                {
#pragma warning disable EXTEXP0001
                    http.RemoveAllResilienceHandlers()
                        .AddStandardResilienceHandler(o =>
                        {
                            o.AttemptTimeout.Timeout = options.Resilience.AttemptTimeout;
                            o.TotalRequestTimeout.Timeout = options.Resilience.TotalRequestTimeout;
                            o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                            o.CircuitBreaker.MinimumThroughput = options.Resilience.MinimumThroughput;
                            o.CircuitBreaker.SamplingDuration = options.Resilience.SamplingDuration;
                            o.CircuitBreaker.BreakDuration = options.Resilience.BreakDuration;
                        });
#pragma warning restore EXTEXP0001
                });
        });
    }

    /// <summary>
    /// Registers HEAppE-related services, HTTP clients, and request handlers with the specified service collection.
    /// </summary>
    /// <remarks>This method configures HTTP clients with appropriate timeouts and resilience policies, and
    /// registers all required HEAppE services and handlers for dependency injection. It is intended to be called during
    /// application startup to ensure all HEAppE integration components are available.</remarks>
    /// <param name="services">The service collection to which the HEAppE integration services and dependencies are added. Cannot be null.</param>
    private static void AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HeappeHttpClientOptions>()
            .BindConfiguration(HeappeHttpClientOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Read eagerly: the client pipelines are configured at registration time. Falls back to the class
        // defaults when the section is absent. Validate the eagerly-read instance with the SAME rules as the
        // registered options (Range attributes + IValidatableObject) so the values applied to the pipelines can
        // never diverge from what ValidateOnStart enforces — a misconfig fails fast with a clear message here.
        var httpOptions = configuration.GetSection(HeappeHttpClientOptions.SectionName).Get<HeappeHttpClientOptions>()
            ?? new HeappeHttpClientOptions();
        Validator.ValidateObject(httpOptions, new ValidationContext(httpOptions), validateAllProperties: true);

        // RemoveAllResilienceHandlers is marked [Experimental] (EXTEXP0001) but is the documented way to drop
        // a handler inherited from ConfigureHttpClientDefaults before adding a client-specific one. See
        // https://learn.microsoft.com/dotnet/core/resilience/http-resilience#remove-standard-resilience-handlers
#pragma warning disable EXTEXP0001
        services.AddHttpClient(HeappeClientName)
            // ServiceDefaults registers a standard resilience handler on ALL clients via
            // ConfigureHttpClientDefaults (AttemptTimeout left at its 10 s default). Without removing it first,
            // that inherited handler stacks on top of ours and its 10 s per-attempt timeout caps every HEAppE
            // call regardless of the values below. Remove the inherited one, then add a single configured handler.
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options => ConfigureDataResilience(options, httpOptions))
            // Every instance sits behind the same Nexus host, so the default per-authority pipeline would let one
            // dead instance open the breaker for all of them; key the pipeline (and its breaker) by identifier.
            .SelectPipelineBy(_ => PipelineKeyFor);

        // Version/health probe: its own client instance with a flat short timeout, NO retries and NO circuit
        // breaker. Drop the inherited global resilience handler so the probe fails fast to the DB version
        // resolver and never shares (or trips) the data client's breaker.
        services.AddHttpClient(HeappeHealthClientName)
            .RemoveAllResilienceHandlers()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(httpOptions.HealthProbeTimeoutSeconds));
#pragma warning restore EXTEXP0001

        // Fail-fast probe client: short timeout, no resilience/retries — we want a dead host to give up quickly.
        services.AddHttpClient(HeappeProbeClientName, c => c.Timeout = TimeSpan.FromSeconds(HeappeProbeTimeout));

        services.AddScoped<HeappeVersionDatabaseResolver>();
        services.AddScoped<IHeappeVersionResolver, HeappeVersionRemoteResolver>();
        // Same remote resolver also answers the liveness gate (segregated interface — the DB resolver cannot).
        services.AddScoped<IHeappeLivenessProbe, HeappeVersionRemoteResolver>();

        services.AddOptions<ClusterAuthTypeOptions>()
            .BindConfiguration(ClusterAuthTypeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRequestHandlers(typeof(HeappeIntegrationExtensions).Assembly);

        services.AddScoped<IKillSwitchStateService, KillSwitchStateService>();
        services.AddScoped<CommandTemplateWriteGuard>();
        services.AddScoped<KillSwitchAdminHandler>();
        services.AddScoped<CommandTemplateManagementService, CommandTemplateManagementService>();
        // One HeappeService behind both of its contracts: the public service and the factory's user authentication.
        services.AddScoped<HeappeService>();
        services.AddScoped<IHeappeService>(sp => sp.GetRequiredService<HeappeService>());
        services.AddScoped<IHeappeUserAuthenticator>(sp => sp.GetRequiredService<HeappeService>());
        services.AddScoped<IHeappeConnectionFactory, HeappeConnectionFactory>();
        services.AddScoped<IHeappeConnectionService, HeappeConnectionService>();
        services.AddScoped<ILocationPartitionsSynchronizationService, LocationPartitionsSynchronizationService>();
        services.AddScoped<IProjectCommandTemplateSyncService, ProjectCommandTemplateSyncService>();
        services.AddScoped<IProjectCreationService, ProjectCreationService>();
        services.AddScoped<IProjectCommandTemplateCreateService, ProjectCommandTemplateCreateService>();

        services.AddResourceValidityAudit();
    }

    private static string PipelineKeyFor(HttpRequestMessage request) =>
        request.Options.TryGetValue(HeappeHttpRequestBuilder.HeappeIdentifierKey, out var identifier) ? identifier : string.Empty;

    /// <summary>
    /// Registers the resource validity audit: its service, the best-effort Teams webhook sender (+ named HTTP
    /// client) and the options bound to <c>Heappe:ResourceAudit</c>. The scheduled worker is added separately as a
    /// hosted service; the on/off switch is the <c>HeappeResourceAudit</c> feature flag.
    /// </summary>
    private static void AddResourceValidityAudit(this IServiceCollection services)
    {
        services.AddScoped<IHeappeResourceAuditService, HeappeResourceAuditService>();
        services.AddScoped<HeappeResourceAuditWebhook>();
        services.AddHttpClient(HeappeResourceAuditWebhook.HttpClientName);

        services.AddOptions<HeappeResourceAuditOptions>()
            .BindConfiguration(HeappeResourceAuditOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>
    /// Resilience options for the HEAppE data client (<see cref="HeappeClientName"/>). Retries are OFF (ADR 0073):
    /// a dead instance is filtered out by the liveness gate before any data call, and an instance that passes the
    /// gate but stalls mid-call must fail in one attempt rather than multiply the wait toward
    /// <see cref="HeappeHttpClientOptions.TotalRequestTimeoutSeconds"/> under the endpoint's 60 s wall.
    /// Extracted so a unit test can assert these options pass startup validation — disabling retries via
    /// <c>MaxRetryAttempts = 0</c> throws <c>OptionsValidationException</c> at boot (the field must be &gt;= 1),
    /// so it is done with a <c>ShouldHandle</c> predicate that never matches instead.
    /// </summary>
    internal static void ConfigureDataResilience(HttpStandardResilienceOptions options, HeappeHttpClientOptions httpOptions)
    {
        // AttemptTimeout: per-request-attempt timeout. HEAppE endpoints (e.g. ListAvailableClusters)
        // can legitimately take longer under load, hence the high default.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(httpOptions.AttemptTimeoutSeconds);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(httpOptions.TotalRequestTimeoutSeconds);
        options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
        // Polly requires SamplingDuration >= 2 × AttemptTimeout to be effective.
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(httpOptions.AttemptTimeoutSeconds * 2);
    }

    /// <summary>
    /// Registers all implementations of IRequestHandler in the specified assembly with the dependency injection
    /// container.
    /// </summary>
    /// <remarks>Each class in the specified assembly that implements IRequestHandler<TRequest, TResponse> is
    /// registered as a scoped service for its corresponding interface. This enables automatic resolution of request
    /// handlers via dependency injection.</remarks>
    /// <param name="services">The service collection to which the request handler services will be added. Cannot be null.</param>
    /// <param name="assembly">The assembly to scan for IRequestHandler implementations. Cannot be null.</param>
    /// <returns>The same IServiceCollection instance so that additional calls can be chained.</returns>
    private static IServiceCollection AddRequestHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerType = typeof(IRequestHandler<,>);

        var handlers = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerType))
            .ToList();

        foreach (var handler in handlers)
        {
            services.AddScoped(handler, handler);
        }

        return services;
    }

    /// <summary>
    /// Configures and registers a background worker that processes work items from a bounded channel. This method sets
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    internal static IHostApplicationBuilder AddBackgroundChannelWorker(this IHostApplicationBuilder builder)
    {
        // Configure bounded channel for backpressure
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        var channel = Channel.CreateBounded<WorkItem>(options);

        builder.Services.AddSingleton(channel);
        builder.Services.AddHostedService<ChannelWorker>();

        return builder;
    }
}