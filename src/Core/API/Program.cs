using System.Diagnostics;

using AspNetCoreRateLimit;

using Core.Shared.Http;

using HashiCorpVault.Contracts.Configuration;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Rewrite;

using Scalar.AspNetCore;

using Serilog;
using Serilog.Events;

using Userorg.Adapter;

using UserOrg.ServiceDefaults;
using UserOrg.ServiceDefaults.Http;
using UserOrg.ServiceDefaults.Logging;
using UserOrg.Shared.FeatureManagement;
using UserOrg.Shared.OpenApi;

using UserOrganizationManagement.API.API;
using UserOrganizationManagement.API.Composition;
using UserOrganizationManagement.API.Middleware;
using UserOrganizationManagement.API.OpenApi;
using UserOrganizationManagement.Domain.Configuration;

#pragma warning disable S1075 // URIs should not be hardcoded
const string _startupLogFilePath = "/opt/API/logs/startup.log";
#pragma warning restore S1075 // URIs should not be hardcoded

const string _v1ApiFlagConfigurationKey = $"FeatureManagement:{FeatureFlags.V1Api}";
const string _v2ApiFlagConfigurationKey = $"FeatureManagement:{FeatureFlags.V2Api}";

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;
await using var bootstrapLogger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: null)
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(), _startupLogFilePath)
    .CreateLogger();
WebApplication? app = null;

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddProductionLoggingDefaults();
    bootstrapLogger.Information("Start Up");

    // Build-time OpenAPI document generation (Microsoft.Extensions.ApiDescription.Server) runs this
    // entrypoint via the GetDocument.Insider host. External infrastructure is unavailable then, so
    // skip configuration sources that reach out — Vault is contacted while building configuration.
    var isOpenApiDocumentGeneration =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    if (isOpenApiDocumentGeneration)
    {
        // Placeholder values for settings the startup path eagerly turns into URIs, so the
        // build-time document generator can build the host without real infrastructure config.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthenticationConfiguration:MetadataAddress"] = "https://openapi-build.invalid",
            ["MailConfiguration:MailSenderUrl"] = "https://openapi-build.invalid",
            // Satisfies the ValidateOnStart DataAnnotations on Heappe ClusterAuthTypeOptions
            // (Waldur/Stomp modules are config-gated off during generation, so need no placeholders).
            ["Heappe:ClusterAuthTypes:AllowedAuthenticationTypeNames:0"] = "SshCertificate",
            ["TokenExchange:Nexus:BaseUrl"] = "https://openapi-build.invalid",
            // The v2 documents are generated from the mapped v2 endpoints; without the flag every
            // build would overwrite the committed API_v2-*.json snapshots with empty documents.
            [_v2ApiFlagConfigurationKey] = "true",
            [_v1ApiFlagConfigurationKey] = "true",
        });
    }

    if (Environment.GetEnvironmentVariable("ASPNETCORE_RUNTYPE_ENVIRONMENT") == "Docker")
    {
        builder.Configuration.AddJsonFile("/opt/API/confs/appsettings.json", false, false);
    }

    if (!isOpenApiDocumentGeneration)
    {
        builder.Configuration.AddVaultSecrets(source =>
        {
            var enabledInstances = builder.Configuration.GetSection("Waldur:Instances")
                .GetChildren()
                .Where(section => section.GetValue("Enabled", false))
                .Select(section => (Name: section["Name"], Path: section.Path))
                .Where(x => !string.IsNullOrEmpty(x.Name))
                .ToList();

            foreach (var instance in enabledInstances)
            {
                source.Mappings.Add(new ExternalCredentialMapping
                {
                    CredentialName = instance.Name,
                    TargetConfigKey = $"{instance.Path}:EndpointApiKey",
                    FieldSelector = cred => cred.ApiKey
                });
                source.Mappings.Add(new()
                {
                    CredentialName = instance.Name,
                    TargetConfigKey = $"{instance.Path}:AuthorizationHeaderValuePrefix",
                    FieldSelector = cred => cred.HeaderPrefix
                });
                source.Mappings.Add(new()
                {
                    CredentialName = instance.Name,
                    TargetConfigKey = $"{instance.Path}:AuthorizationHeader",
                    FieldSelector = cred => cred.AuthorizationHeader
                });
            }
        });
    }

    builder.AddHostInfrastructure();

    // V1Api is default-on, and an undefined flag reads as disabled through IFeatureManager, which would
    // 404 every v1 route on a deployment whose appsettings predates the flag. Seed the default so the
    // runtime EnabledWhen gate agrees with the startup gate below. Ceiling: the seed is the last
    // configuration source, so a key added to the JSON file after startup no longer wins a reload —
    // deployments that want to flip v1 at runtime must declare the flag up front.
    var isV1ApiConfigured = builder.Configuration[_v1ApiFlagConfigurationKey] is not null;

    if (!isV1ApiConfigured)
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [_v1ApiFlagConfigurationKey] = "true",
        });
    }

    var isV1ApiEnabled = builder.Configuration.GetValue(_v1ApiFlagConfigurationKey, true);

    builder.AddV1Core(isV1ApiEnabled);

    // Microsoft.FeatureManagement's IFeatureManager is async and only exists once the container is
    // built, so the composition gate reads the flag straight from configuration — same as ReadOnlyMode
    // in AddHostInfrastructure. Read once at startup and threaded through both v2 wiring points.
    var isV2ApiEnabled = builder.Configuration.GetValue<bool>(_v2ApiFlagConfigurationKey);

    builder.AddV2Core(isV2ApiEnabled);

    //var vaultConf = new VaultConfiguration();
    //builder.Configuration.GetSection(VaultConfiguration.path).Bind(vaultConf);

    builder.AddModules();

    // Event-driven Waldur.Sync (producer worker + in-process consumer). Config-gated on
    // Waldur:Instances.
    builder.Services.AddWaldurSyncModule(builder.Configuration);

    if (isOpenApiDocumentGeneration)
    {
        // Build-time OpenAPI generation starts the host (StartAsync), which would run the migration/
        // seed hosted services and hit the database. The document doesn't need them — drop all
        // hosted services so generation stays infrastructure-free.
        foreach (var descriptor in builder.Services
                     .Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                     .ToList())
        {
            builder.Services.Remove(descriptor);
        }
    }

    app = builder.Build();
    app.UseTrustedProxyForwardedHeaders();
    var scalarOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ScalarUiOptions>>().Value;
    app.Logger.LogInformation("Scalar config: {@conf}", scalarOptions);
    var scalarOidc = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ScalarOidcOptions>>().Value;

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseStepUpRequestLogging();

    app.UseCors(HostInfrastructureExtensions.DefaultCorsName);
    app.UseExceptionHandler();

    // Enable authentication/authorization middleware so the app can accept Gateway internal JWTs
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCoreConditionalGet();
    app.UseOutputCache();
    app.UseMiddleware<RequestSizeMiddleware>();
    app.UseMiddleware<FeatureFlagMiddleware>();

    app.UseMiddleware<OtelTraceResponseMiddleware>();

    app.UseStatusCodePages();
    app.UseIpRateLimiting();

    if (isV1ApiEnabled)
    {
        app.MapV1CoreEndpoints();
    }

    app.RegisterDataStagingLocationEndpoint();
    app.RegisterVersionEndpoint();

    app.MapDefaultEndpoints();
    app.MapModules();

    if (isV2ApiEnabled)
    {
        app.MapV2CoreEndpoints();
    }

    var scalarRoute = $"/{scalarOptions.RoutePrefix}";
    var publicPathBase = string.IsNullOrEmpty(scalarOptions.PublicPathBase) ? string.Empty : $"/{scalarOptions.PublicPathBase}";
    var publicScalarRoute = $"{publicPathBase}{scalarRoute}";

    // Documentation is freely viewable — no auth required to read the spec or open Scalar. RBAC is
    // enforced by the Gateway at call time, not at documentation time.
    app.MapOpenApi($"{scalarRoute}/{{documentName}}/openapi.json").AllowAnonymous();

    // Scalar UI served at the configured route prefix. AddDocuments explicitly registers
    // document names so Scalar does not fall back to the default 'v1'.
    app.MapScalarApiReference(scalarRoute, options =>
    {
        options
            .WithTitle(scalarOptions.Title)
            .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
            .WithOpenApiRoutePattern($"{publicScalarRoute}/{{documentName}}/openapi.json")
            .AddDocuments(ApiDocuments.Published(isV1ApiEnabled));

        options.WithLoginSchemes(scalarOidc);
        // Prefill the "Service " prefix so users only paste the token (ApiKey schemes get no auto-prefix).
        options.AddApiKeyAuthentication("Service", apiKey => apiKey.Value = "Service ");
        options.Theme = ScalarTheme.BluePlanet;
        options.DotNetFlag = true;

    }).AllowAnonymous();

    var option = new RewriteOptions();
    option.AddRedirect("^$", publicScalarRoute);
    app.UseRewriter(option);

    await app.StartAsync();
    await app.WaitForShutdownAsync();
}
catch (Exception ex)
{
    if (app is not null)
    {
        app.Logger.LogCritical(ex, "Application terminated unexpectedly");
    }
    else
    {
        bootstrapLogger.Fatal(ex, "Application terminated unexpectedly");
    }

    throw;
}
finally
{
    if (app is not null)
    {
        await app.DisposeAsync();
    }
}
