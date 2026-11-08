using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.Adapters;
using WorkflowEngine.Core.Application;
using WorkflowEngine.Core.Features.Retry;
using WorkflowEngine.Core.Features.Submit;
using WorkflowEngine.Core.Persistence;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.DependencyInjection;

/// <summary>
/// Registers all WorkflowEngine module services into the DI container.
/// Usage:
/// <code>
/// builder.AddWorkflowEngine();
/// // register strategies and handlers separately:
/// builder.Services.AddSingleton&lt;IRequestStrategy, MyStrategy&gt;();
/// builder.Services.AddStepHandler&lt;MyStepRequest, MyStepHandler&gt;();
/// </code>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers WorkflowEngine module services including persistence, workers, and the application service.
    /// Binds options from <c>appsettings.json</c> section <c>"WorkflowEngine"</c>.
    /// </summary>
    public static void AddWorkflowEngine(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<WorkflowEngineOptions>(
            builder.Configuration.GetSection(WorkflowEngineOptions.SectionName));

        RegisterPersistence(builder);
        RegisterApplication(builder.Services);
        RegisterWorkers(builder.Services);
    }

    private static void RegisterPersistence(IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("WorkflowDBContext");

        builder.Services.AddPooledDbContextFactory<WorkflowDbContext>(opt =>
        {
            // Inlined rather than UserOrg.Shared's ConfigureSqlCommandLogging (ADR-0076): WorkflowEngine.Core
            // stays domain-agnostic and standalone (ADR-0013) and must not gain a ProjectReference to
            // UserOrg.Shared.
            if (!builder.Environment.IsDevelopment())
            {
                opt.ConfigureWarnings(w => w.Ignore(
                    RelationalEventId.CommandExecuting,
                    RelationalEventId.CommandExecuted));
            }

            opt.UseSqlServer(connectionString, sqlOpt =>
            {
                sqlOpt.MigrationsAssembly(typeof(WorkflowDbContext).Assembly.FullName);
                sqlOpt.MigrationsHistoryTable("__EFMigrationsHistory", WorkflowDbContext.SchemaName);
            });
        });

        builder.EnrichSqlServerDbContext<WorkflowDbContext>(settings => settings.DisableTracing = true);

        builder.Services.AddHostedService<WorkflowMigrationStartupService>();
        builder.Services.AddScoped<IRequestRepository, WorkflowDbContextRepository>();
    }

    private static void RegisterApplication(IServiceCollection services)
    {
        services.AddSingleton<IStrategyRegistry, StrategyRegistryImpl>();

        // In-process queue backed by a bounded Channel<StepRequest>
        services.AddSingleton<IQueuePort>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowEngineOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<ChannelQueueAdapter>>();
            return new ChannelQueueAdapter(opts.QueueCapacity, logger);
        });

        // Feature-slice handlers (scoped — they use IRequestRepository which is scoped)
        services.AddScoped<SubmitHandler>();
        services.AddScoped<RetryHandler>();

        services.AddScoped<RequestApplicationService>();
        services.AddScoped<IRequestApplicationService>(sp =>
            sp.GetRequiredService<RequestApplicationService>());

        services.AddScoped<IWorkflowClient, WorkflowClient>();
    }

    private static void RegisterWorkers(IServiceCollection services)
    {
        services.AddHostedService<RequestWorkerStartupRecovery>();
        services.AddHostedService<RequestWorker>();
        services.AddHostedService<RetryScheduledScanner>();
    }

    /// <summary>
    /// Registers the WorkflowEngine application layer and background workers
    /// without any persistence or transport infrastructure.
    /// Use this when providing your own adapters (e.g. an in-memory repository for demos or tests).
    /// </summary>
    /// <remarks>
    /// The caller is responsible for registering the following before building the container:
    /// <list type="bullet">
    ///   <item><see cref="IRequestRepository"/> — persistence adapter</item>
    ///   <item><see cref="ICallbackPort"/> — outbound notification adapter</item>
    ///   <item><see cref="Microsoft.Extensions.Options.IOptions{T}"/> of <see cref="WorkflowEngineOptions"/> — module settings</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddWorkflowEngineApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Register default options so callers don't need to reference the internal WorkflowEngineOptions type.
        services.Configure<WorkflowEngineOptions>(_ => { });
        RegisterApplication(services);
        RegisterWorkers(services);
        return services;
    }

    /// <summary>
    /// Registers a step handler and its non-generic <see cref="IStepHandlerInvoker"/> so the
    /// WorkflowEngine worker can dispatch to it <b>without reflection</b>.
    /// </summary>
    /// <typeparam name="TRequest">The <see cref="StepRequest"/> type this handler processes.</typeparam>
    /// <typeparam name="THandler">The concrete handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">DI lifetime — defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    public static IServiceCollection AddStepHandler<TRequest, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TRequest : StepRequest
        where THandler : class, IStepHandler<TRequest>
    {
        ArgumentNullException.ThrowIfNull(services);

        // Typed handler — allows direct resolution by strategy or test code
        services.Add(new ServiceDescriptor(typeof(IStepHandler<TRequest>), typeof(THandler), lifetime));

        // Untyped invoker keyed by the request type — used by RequestWorker without reflection
        services.AddKeyedScoped<IStepHandlerInvoker, StepHandlerInvoker<TRequest>>(typeof(TRequest));

        return services;
    }
}