using Core.DataAccess.ResourceRequests;

using Core.Features.Country;
using Core.Features.Enums.GetAuthenticationTypes;
using Core.Features.Project;
using Core.Features.Project.AssignProjectResource;
using Core.Features.Project.GetProjectTemplates;
using Core.Features.Provider.ResourceAssignment;
using Core.Features.Request.Notifications;
using Core.Features.Resource.Workflow;
using Core.Features.ResourceRequest.Activation;
using Core.Features.ResourceRequest.Expiry;
using Core.Features.ResourceRequest.Flow;
using Core.Features.Statistics;
using Core.Features.User.InactiveUserSync;
using Core.Features.UserInfo;
using Core.Shared.OutputCache;
using Core.Shared.Paging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using WorkflowEngine.Core.DependencyInjection;

namespace Core.Composition;

public static class ServiceCollectionExtensions
{
    // Requires app.UseOutputCache() in Program.cs after UseAuthorization().
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Testable clock for time-dependent endpoints (e.g. statistics weekly bucketing).
        services.TryAddSingleton(TimeProvider.System);

        // Concrete domain event handlers (logging) are auto-registered by AddCoreDataAccess via
        // assembly scan over Core.Domain (D8). Only the cache-eviction handlers below — which touch
        // the output cache, an infrastructure concern owned by Core — are registered here.
        services.AddOutputCache(options =>
        {
            options.AddPolicy(UserInfoCache.PolicyName, new CoreCachePolicy(UserInfoCache.DescribeAsync));
            options.AddPolicy(ProjectCache.PolicyName, new CoreCachePolicy(ProjectCache.Describe));
            options.AddPolicy(ProjectTemplatesCache.PolicyName, new CoreCachePolicy(ProjectTemplatesCache.Describe));
            options.AddPolicy(CountryCache.PolicyName, new CoreCachePolicy(CountryCache.Describe));
            options.AddPolicy(StatisticsCache.PolicyName, new CoreCachePolicy(StatisticsCache.Describe));
        });
        services.AddSingleton<IOutputCacheInvalidator, OutputCacheInvalidator>();

        // Cache invalidation via domain events. UserInfo is evicted on any user-scoped change;
        // Project on create/delete. (UserCreatedEvent has no cache to evict, so it is omitted.)
        services.AddScoped<IDomainEventHandler<UserDeletedEvent>, UserInfoCacheEvictionHandler<UserDeletedEvent>>();
        services.AddScoped<IDomainEventHandler<UserReactivatedEvent>, UserInfoCacheEvictionHandler<UserReactivatedEvent>>();
        services.AddScoped<IDomainEventHandler<UserNameUpdatedEvent>, UserInfoCacheEvictionHandler<UserNameUpdatedEvent>>();
        // Email is part of output-cached UserInfoResponse, so evict on change (ADR-0068).
        services.AddScoped<IDomainEventHandler<UserEmailUpdatedEvent>, UserInfoCacheEvictionHandler<UserEmailUpdatedEvent>>();
        services.AddScoped<IDomainEventHandler<UserPreferencesUpdatedEvent>, UserInfoCacheEvictionHandler<UserPreferencesUpdatedEvent>>();
        services.AddScoped<IDomainEventHandler<UserPromotedToProviderAdminEvent>, UserInfoCacheEvictionHandler<UserPromotedToProviderAdminEvent>>();
        services.AddScoped<IDomainEventHandler<UserDemotedToUserEvent>, UserInfoCacheEvictionHandler<UserDemotedToUserEvent>>();
        services.AddScoped<IDomainEventHandler<UserAssignedToProviderEvent>, UserInfoCacheEvictionHandler<UserAssignedToProviderEvent>>();
        services.AddScoped<IDomainEventHandler<UserUnassignedFromProviderEvent>, UserInfoCacheEvictionHandler<UserUnassignedFromProviderEvent>>();
        services.AddScoped<IDomainEventHandler<ProjectCreatedEvent>, ProjectCacheEvictionHandler<ProjectCreatedEvent>>();
        services.AddScoped<IDomainEventHandler<ProjectDeletedEvent>, ProjectCacheEvictionHandler<ProjectDeletedEvent>>();

        // Resource-assignment change (ADR-0016): evict the resource-tagged responses (templates) and
        // trigger a HEAppE sync per project using the resource. Both run alongside the logging handler.
        services.AddScoped<IDomainEventHandler<ResourceAssignmentChangedEvent>, ResourceCacheEvictionHandler<ResourceAssignmentChangedEvent>>();
        services.AddScoped<IDomainEventHandler<ResourceAssignmentChangedEvent>, ResourceSyncHandler>();

        // Resource↔project assignment change (F5): trigger a HEAppE sync for the single affected project. The
        // endpoint evicts the project + templates cache inline, so only the sync handler runs post-commit.
        services.AddScoped<IDomainEventHandler<ProjectResourceAssignmentChangedEvent>, ProjectResourceSyncHandler>();

        // Resource became available (DEC-4): resume every assignment request parked on the ResourceAvailability
        // gate for that resource key. Outbox-dispatched, mirrors ProjectResourceSyncHandler.
        services.AddScoped<IDomainEventHandler<ProjectResourceActivatedEvent>, ProjectResourceActivatedEventHandler>();

        // Pure create-and-allocate domain service (ADR-0043). Stateless, no I/O — registered as a singleton.
        services.AddSingleton<IResourceProvisioningService, ResourceProvisioningService>();

        // Configurable offset-paging bounds for growable v2 list endpoints (ADR-0020). Defaults 100/300.
        services.AddOptions<PaginationOptions>().BindConfiguration(PaginationOptions.SectionName);

        services.AddOptions<AuthenticationTypeOptions>().BindConfiguration(AuthenticationTypeOptions.ConfigurationPath);

        // Request self-onboarding action links (ADR-0009), produced inline after save.
        services.AddOptions<ActionLinkOptions>().BindConfiguration(ActionLinkOptions.ConfigurationPath);
        // Action-link settings for the resource-assignment request flow (ADR-0064); mirrors ActionLinkOptions.
        services.AddOptions<ResourceRequestActionLinkOptions>().BindConfiguration(ResourceRequestActionLinkOptions.ConfigurationPath);
        services.AddScoped<INotificationSender, MailNotificationSender>();
        services.AddScoped<IActionLinkGenerator, ActionLinkGenerator>();

        services.AddCoreResourceWorkflow();

        // The assign-resource-to-project flow (ADR-0063/0064/0065). AddFlow compiles the DSL and registers the
        // definition plus its step handlers, which write through the projection store below. Both are plain
        // singleton/handler registrations, so neither depends on the host's AddWorkflowEngine call running first.
        services.AddResourceRequestProjectionStore();
        services.AddFlow<AssignResourceFlow>();

        // Expiry sweeper for requests parked past their configured duration in either gate (DEC-7). Reuses the
        // flow's cancel signal path, so it needs no wiring beyond the options + the poll-loop hosted service.
        services.AddOptions<ResourceRequestExpiryOptions>().BindConfiguration(ResourceRequestExpiryOptions.ConfigurationPath);
        services.AddHostedService<ResourceRequestExpirySweeper>();

        // Inactive user sync (mark users as deleted when reported by DDI/DRE)
        services.AddOptions<InactiveUserSyncOptions>()
            .BindConfiguration(InactiveUserSyncOptions.ConfigurationPath)
            .Validate(
                o => !o.Enabled
                    || (Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _)
                        && !string.IsNullOrWhiteSpace(o.ClientId)
                        && !string.IsNullOrWhiteSpace(o.ClientSecret)),
                "InactiveUserSync: BaseUrl (absolute URL), ClientId and ClientSecret are required when Enabled=true")
            .Validate(
                o => o.SyncInterval > TimeSpan.Zero,
                "InactiveUserSync: SyncInterval must be positive")
            .ValidateOnStart();

        var inactiveUserSyncConfig = configuration.GetSection(InactiveUserSyncOptions.ConfigurationPath)
            .Get<InactiveUserSyncOptions>();
        if (inactiveUserSyncConfig?.Enabled == true)
        {
            services.AddHostedService<InactiveUserSync>();
        }

        return services;
    }
}