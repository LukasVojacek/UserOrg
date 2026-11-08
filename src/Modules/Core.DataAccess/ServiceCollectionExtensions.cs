using Core.DataAccess.Diagnostics;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.Persistance;

using WellKnownRoles = Core.Domain.User.WellKnownRoles;

namespace Core.DataAccess;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// See the <c>opts.CommandTimeout</c> call site below for why 30s.
    /// </summary>
    private const int CommandTimeoutSeconds = 30;

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Register Core.DataAccess services:
        /// - DatabaseContext (internal EF DbContext implementation)
        /// - IUnitOfWork (public interface for persistence + event dispatch)
        /// - IRepository{TAggregate, TKey} (public interface for aggregate repositories)
        ///   * IRepository{Project, ProjectId} (concrete implementation in DatabaseContext.Repository.cs)
        /// - IDataQuery{TEntity} (public interface for read-only untracked queries)
        /// - Domain event infrastructure (internal DomainEventChannel, DomainEventDispatcherService)
        /// - IDomainEventHandler{T} (public interface with default LoggingDomainEventHandler implementation)
        /// </summary>
        public IServiceCollection AddCoreDataAccess(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            // DatabaseContext (registered below) resolves IOptions<WellKnownRoles> for its role lookups
            // (Contexts/DatabaseContext.UserRepository.cs). This binding belongs here in the always-
            // registered bucket rather than in Core.Composition's AddCore, which may be conditionally
            // registered and thus cannot be relied upon. IOptions<T> degrades silently to type defaults
            // when unbound instead of throwing, so a missed binding would corrupt role lookups with no
            // startup failure.
            services.Configure<WellKnownRoles>(configuration);

            // AddLogging is TryAdd-based (never overrides a host-configured provider) — belongs here because
            // BlockedReadDiagnosticsInterceptor below is the first thing in this module to need ILogger<T>
            // resolvable purely from what AddCoreDataAccess itself registers, rather than assuming the caller
            // already wired logging (true for the ASP.NET host, not for a minimal test ServiceCollection).
            services.AddLogging();

            // Blocked-read diagnostics (D2a): names the blocker instead of the read stalling for the full
            // command timeout and failing opaquely. See ServiceCollectionExtensions.CommandTimeoutSeconds
            // for the timeout this backs.
            services.AddSingleton<IBlockedReadClassifier, SqlBlockedReadClassifier>();
            services.AddScoped<BlockedReadDiagnosticsInterceptor>();

            services.AddDbContext<DatabaseContext>((serviceProvider, optionsBuilder) =>
            {
                var connectionString = configuration.GetConnectionString("UserOrgDBContext")
                    ?? throw new InvalidOperationException("Connection string 'UserOrgDBContext' not found");

                optionsBuilder
                    .UseSqlServer(connectionString, opts =>
                    {
                        opts.MigrationsAssembly(typeof(DatabaseContext).Assembly);

                        // Was 300s: under lock-based READ COMMITTED a reader blocked by any open writer waited
                        // the full five minutes and then failed opaquely, naming neither the blocking session
                        // nor the query — the incident this diagnostic exists to prevent a repeat of. 30s
                        // matches the timeout every sibling context in this codebase already uses
                        // (Outbox/ResourceRequests/HeappeIntegration persistence extensions) and is generous for
                        // this endpoint's reads (a FindAsync-by-key and an existence check, both indexed
                        // point lookups) while failing more than an order of magnitude sooner than the old
                        // value — well inside a caller's own request timeout instead of past it.
                        opts.CommandTimeout(CommandTimeoutSeconds);
                    });

                optionsBuilder.AddInterceptors(serviceProvider.GetRequiredService<BlockedReadDiagnosticsInterceptor>());

                // Fail fast if the model drifts from the migration snapshot. This context is the canonical
                // dbo migration owner (ADR-0049) and must stay zero-diff; a pending model change means the
                // model diverged from the last migration — surface it at startup, not as silent schema drift.
                optionsBuilder.ConfigureWarnings(w => w.Throw(RelationalEventId.PendingModelChangesWarning));

                var isDevelopment = serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment();

                // EnableDetailedErrors / EnableSensitiveDataLogging surface SQL parameter values
                // (emails, names) in logs — dev diagnostics only, never in a deployed environment.
                if (isDevelopment)
                {
                    optionsBuilder
                        .EnableDetailedErrors()
                        .EnableSensitiveDataLogging();
                }

                optionsBuilder.ConfigureSqlCommandLogging(isDevelopment);
            });

            // Modular context owns the dbo migrations (ADR-0049). Checks pending at startup and,
            // when Database:AutoMigrateOnStartup is set, applies them. Replaces the legacy migrator.
            services.AddHostedService<Contexts.CoreDatabaseMigrationStartupService>();

            // Register open-generic IDataQuery<T> — read-only untracked access per entity type
            services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));

            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.Project.Project, ProjectShortName>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.Provider.Provider, ProviderName>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<Domain.Provider.IProviderRepository>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.Provider.ProjectResource, ProjectResourceKey>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.Request.Request, Email>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.Request.Request, RequestId>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.OnboardingRequest.OnboardingRequest, Domain.OnboardingRequest.OnboardingRequestSlug>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.User.User, UserName>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.User.User, IdpId>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IProjectDomainTypeLookup>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IProjectDirectory>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IRepository<Domain.Project.ProjectTag, TagName>>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<IUserDirectory>(sp => sp.GetRequiredService<DatabaseContext>());
            services.AddScoped<DatabaseContext>();

            services.AddScoped<IUserInfoQuery, UserDetailsQuery>();
            services.AddScoped<IProjectQuery, ProjectDetailsQuery>();
            services.AddScoped<IProviderQuery, ProviderDetailsQuery>();
            services.AddScoped<IRequestQuery, RequestDetailsQuery>();
            services.AddScoped<IOnboardingRequestQuery, OnboardingRequestQuery>();
            services.AddScoped<ILocationQuery, LocationDetailsQuery>();
            services.AddScoped<IProjectTagQuery, ProjectTagDetailsQuery>();
            services.AddScoped<ICountryQuery, CountryQuery>();
            services.AddScoped<IStatisticsQuery, StatisticsQuery>();
            services.AddScoped<IResourceQuery, ResourceDetailsQuery>();
            services.AddScoped<IResourceAssignmentQuery, ResourceAssignmentQuery>();
            services.AddScoped<IProjectResourceAssignmentQuery, ProjectResourceAssignmentQuery>();
            services.AddScoped<IHeappeAuditTargetQuery, HeappeAuditTargetQuery>();
            services.AddScoped<IDdiProjectsInfoQuery, DdiProjectsInfoQuery>();
            services.AddScoped<ISyncDetailsQuery, SyncDetailsQuery>();

            // The audit-target query reads "now" from the clock (validity window). TryAdd so the host's own
            // TimeProvider.System registration (Core composition) wins when present.
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<DomainEventChannel>();
            services.AddHostedService<DomainEventDispatcherService>();
            services.AddTransient(typeof(IDomainEventHandler<>), typeof(LoggingDomainEventHandler<>));

            // Auto-register every concrete domain event handler from the Core.Domain assembly (D8).
            // Each closed IDomainEventHandler<TEvent> implementation is registered against its closed
            // interface, so the dispatcher resolves it via GetServices<IDomainEventHandler<TEvent>>
            // alongside the open-generic logging handler above. Adding a new handler needs no manual wiring.
            var handlerInterface = typeof(IDomainEventHandler<>);
            foreach (var handlerType in handlerInterface.Assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }))
            {
                foreach (var closedInterface in handlerType.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface))
                {
                    services.AddScoped(closedInterface, handlerType);
                }
            }

            return services;
        }
    }
}