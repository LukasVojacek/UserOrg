using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Primitives;
using Core.Domain.Project;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Domain.User;
using Core.Features.Request.Notifications;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;
using Core.Features.ResourceRequest.Activation;
using Core.Features.ResourceRequest.Flow;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Ports;

using ELocationType = Core.Domain.Shared.ELocationType;
using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>
/// Variant B acceptance test — the regression the plan calls out explicitly (criteria 4 + 5), end to end over
/// the real engine: the PI approves a request naming a resource that has NO <see cref="ResourceAggregate"/> row
/// at all, the flow parks it on the <c>ResourceAvailability</c> gate, and it wakes to <c>Assigned</c> once that
/// resource is created through the real production path (<see cref="InitializeProjectResourceHandler"/> →
/// <see cref="IResourceProvisioningService"/> → <see cref="ResourceAggregate.Create"/>). Only the outbox
/// dispatch is stood in for: the raised event is handed straight to the real
/// <see cref="ProjectResourceActivatedEventHandler"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AssignResourceFlowPlainCreateWakesRequestTests
{
    private const string Slug = "proj-newgpu";
    private const string ProviderNameValue = "acme";
    private const string LocationNameValue = "gpu-loc";
    private const string ResourceNameValue = "newgpu";
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class InMemoryFactory(string storeName) : IDbContextFactory<ResourceRequestsDbContext>
    {
        public ResourceRequestsDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ResourceRequestsDbContext>().UseInMemoryDatabase(storeName).Options);
    }

    /// <summary>Dictionary-backed <see cref="IRepository{TAggregate,TKey}"/> shared by resource creation and attach.</summary>
    private sealed class MutableResourceRepository : IRepository<ResourceAggregate, ProjectResourceKey>
    {
        private readonly Dictionary<ProjectResourceKey, ResourceAggregate> _items = [];

        public IReadOnlyCollection<ResourceAggregate> Added => _items.Values;

        public ValueTask<ResourceAggregate?> FindAsync(ProjectResourceKey key, CancellationToken ct) =>
            new(_items.GetValueOrDefault(key));

        public ValueTask<ResourceAggregate?> AddAsync(ResourceAggregate aggregate, CancellationToken ct)
        {
            _items[aggregate.Key] = aggregate;
            return new(aggregate);
        }

        public void Delete(ResourceAggregate aggregate, CancellationToken ct) { }
    }

    /// <summary>Collects the events raised by whatever was added to <see cref="MutableResourceRepository"/> this save.</summary>
    private sealed class CapturingUnitOfWork(MutableResourceRepository resources) : IUnitOfWork
    {
        public IReadOnlyList<IDomainEvent> CollectedEvents { get; private set; } = [];

        public IRepository<TAggregate, TKey> GetRepository<TAggregate, TKey>() where TAggregate : AggregateRoot =>
            throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            CollectedEvents = [.. resources.Added.SelectMany(r => r.DomainEvents)];
            return Task.FromResult(1);
        }
    }

    private sealed class NoopNotifications : INotificationSender
    {
        public Task SendPiApprovalRequestedAsync(Email piEmail, string requestSlug, string actionToken, ProjectShortName project, ResourceName resource, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequesterAssignedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequesterRejectedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, string? reason, CancellationToken ct) => Task.CompletedTask;
        public Task SendProviderResourceNeededAsync(Email providerSupportEmail, string requestSlug, ProjectShortName project, ResourceName resource, Email requesterContact, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, string actionToken, ResourceName resourceName, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestCreatedToOwnerAsync(Email ownerEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestAcceptedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestRejectedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendResourcePublishedAsync(Email recipientEmail, string resourceName, string projectShortname, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestToCreateAccountAsync(Email recipientEmail, string resourceName, List<(string, string, string, string)> clusters, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact(DisplayName = "PI approves a resource that doesn't exist → parks → creating that resource via the real Create() path wakes it to Assigned")]
    public async Task Should_ParkThenWakeToAssigned_When_ApprovedResourceIsCreatedThroughRealPath()
    {
        var ct = TestContext.Current.CancellationToken;

        var projectionFactory = new InMemoryFactory(Guid.NewGuid().ToString());
        var store = new ResourceRequestProjectionStore(projectionFactory);
        var query = new ProjectResourceRequestQuery(projectionFactory);
        await store.InsertAsync(new ProjectResourceRequest
        {
            Slug = Slug,
            ProjectShortName = "proj",
            ProviderName = ProviderNameValue,
            ResourceName = ResourceNameValue,
            RequesterIdpId = "idp-1",
            ContactEmail = "pi@example.org",
            State = ResourceRequestState.PendingPiApproval,
            WorkflowRequestId = Guid.Empty,
            CreatedAt = Now,
            UpdatedAt = Now,
        }, ct);
        var repository = new InMemoryWorkflowRepository();

        var project = ProjectAggregate.Create(
            ProjectShortName.Create("proj"), ProjectName.Create("Project"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            Now, Now.AddYears(1), ServiceName.Default);

        var provider = ProviderAggregate.Create(
            ProviderName.From(ProviderNameValue), countryId: 1, supportEmail: Email.Create("support@acme.org"));
        Assert.True(provider.AddLocation(LocationName.From(LocationNameValue), "https://gpu", ELocationType.HPC, isShared: true).IsSuccess);

        var users = new Mock<IUserInfoQuery>();
        users.Setup(u => u.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetails(IdpId.Create("idp-1"), Email.Create("requester@example.org"), UserName.Create("requester")));

        var resources = new MutableResourceRepository();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestRepository>(repository);
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<AssignResourceFlow>();

        services.AddSingleton<IResourceRequestProjectionStore>(store);
        services.AddSingleton<IProjectResourceRequestQuery>(query);
        services.AddSingleton<IOptions<ResourceRequestActionLinkOptions>>(
            Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 }));
        services.AddSingleton<INotificationSender, NoopNotifications>();
        services.AddSingleton(users.Object);
        services.AddSingleton<IRepository<ProjectAggregate, ProjectShortName>>(new FakeRepository<ProjectAggregate, ProjectShortName>(project));
        services.AddSingleton<IRepository<ResourceAggregate, ProjectResourceKey>>(resources);
        services.AddSingleton<IProviderRepository>(new FakeProviderRepository(provider));
        services.AddSingleton(new Mock<IUnitOfWork>().Object);

        await using var serviceProvider = services.BuildServiceProvider();
        var workers = serviceProvider.GetServices<IHostedService>().ToList();
        foreach (var worker in workers)
        {
            await worker.StartAsync(CancellationToken.None);
        }

        try
        {
            Guid requestId;
            using (var scope = serviceProvider.CreateScope())
            {
                requestId = await scope.ServiceProvider.GetRequiredService<IWorkflowClient>()
                    .StartAsync<AssignResourceFlow>(new AssignResourceFlowInput(Slug));
            }

            using var runScope = serviceProvider.CreateScope();
            var client = runScope.ServiceProvider.GetRequiredService<IWorkflowClient>();

            // Criterion 4: the PI approves while no ProjectResource row exists for the requested name at all.
            await FlowEngineWaits.WaitForGateAsync(repository, requestId, "PiApproval");
            await client.ResumeAsync(requestId, new PiDecision(PiDecisionKind.Approve));

            await FlowEngineWaits.WaitForGateAsync(repository, requestId, "ResourceAvailability");
            Assert.Equal(ResourceRequestState.AwaitingResourceCreation, (await store.GetBySlugAsync(Slug, ct))!.State);

            // Criterion 5: create the resource through the REAL production entry point a create-resource
            // submission drives — ResourceProvisioningService.Provision → ProjectResource.Create.
            var uow = new CapturingUnitOfWork(resources);
            var initializeHandler = new InitializeProjectResourceHandler(
                new ResourceProvisioningService(), uow, resources, new FakeProviderRepository(provider),
                NullLogger<InitializeProjectResourceHandler>.Instance);

            var step = new InitializeProjectResourceStep(Guid.NewGuid(), provider.Name, CreateRequest(), new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        }, CreatedBy: UserId.Create(Guid.NewGuid()));
            await initializeHandler.HandleAsync(step, ct);

            var activated = Assert.IsType<ProjectResourceActivatedEvent>(
                Assert.Single(uow.CollectedEvents.OfType<ProjectResourceActivatedEvent>()));

            // Dispatch exactly what the real outbox dispatcher would: hand the raised event to the real handler.
            var activationHandler = new ProjectResourceActivatedEventHandler(
                query, client, NullLogger<ProjectResourceActivatedEventHandler>.Instance);
            await activationHandler.HandleAsync(activated, ct);

            await FlowEngineWaits.WaitForStatusAsync(repository, requestId, RequestStatus.Completed);

            Assert.Equal(ResourceRequestState.Assigned, (await store.GetBySlugAsync(Slug, ct))!.State);
            Assert.Single(project.Resources);
        }
        finally
        {
            foreach (var worker in workers)
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }
    }

    private static CreateResourceOnLocationEndpoint.Request CreateRequest() => new(
        Name: ResourceNameValue, Description: "desc", PrimaryInvestigatorEmail: "pi@example.org",
        StartDate: Now, EndDate: Now.AddYears(1), AccountString: "ACC-1", UnitType: EAllocationUnitType.NodeHours,
        MappingOneToOne: true, UseAccountingStringForScheduler: false, Usernames: new List<string>() { "user@example.org" }, LicenseUrl: null, BypassHeappe: false,
        Clusters: [new(ClusterName: "cluster-1", ScratchPath: "/scratch", ProjectPath: "/project")],
        AssignedLocations:
        [
            new(LocationName: LocationNameValue, AllocationUnitType: EAllocationUnitType.NodeHours, AggregationName: null,
                AllocationUnitCount: 100, AllocationDescription: "alloc", PreferredAuthenticationType: "pwd", Specifications: null)
        ]);
}
