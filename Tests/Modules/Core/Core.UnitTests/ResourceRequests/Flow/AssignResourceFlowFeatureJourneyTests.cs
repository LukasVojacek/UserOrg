using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Primitives;
using Core.Domain.Project;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Domain.User;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.Activation;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.HandleResourceRequestAction;
using Core.Features.ResourceRequest.PublicAction;
using Core.Features.ResourceRequest.ResendApprovalEmail;
using Core.Shared.Auth;
using Core.UnitTests.Shared;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Ports;

using ProjectAggregate = Core.Domain.Project.Project;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>
/// B10 feature-level verification: drives the assign-resource journey over the REAL engine worker + gates AND the
/// REAL projection persistence (schema-isolated <see cref="ResourceRequestsDbContext"/> in-memory) and the REAL
/// read query — the seams B04's e2e stubs with a fake store. It exercises the cross-block chain that no single
/// block test covers end to end:
/// <list type="bullet">
///   <item>SendPiApprovalEmail mints a token, hashes it, and writes the hash to the real projection row (B04→B02).</item>
///   <item>The minted plaintext token resolves back through the real B06 token resolver over the real query
///   (mint→hash→find→verify roundtrip, in-memory DB).</item>
///   <item>Approve with the resource missing parks on the ResourceAvailability gate; a real
///   <see cref="ProjectResourceActivatedEvent"/> fed to the real
///   <see cref="ProjectResourceActivatedEventHandler"/> resumes it (B04→B02).</item>
///   <item>AttachResource lands the F5 assignment link on the project aggregate and the projection at Assigned.</item>
///   <item>The requester/PI/provider notifications fire in the expected order.</item>
/// </list>
/// The SQL filtered-unique-index (ADR-0066) is NOT asserted here — the EF in-memory provider does not honour
/// filtered indexes; that guard remains a live-Aspire verification item (implementation-log B02 HIGH-2).
/// </summary>
[Trait("Category", "Integration")]
public sealed class AssignResourceFlowFeatureJourneyTests
{
    private const string Slug = "proj-gpu";
    private const string Provider = "acme";
    private const string Resource = "gpu";
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private sealed class InMemoryFactory(string storeName) : IDbContextFactory<ResourceRequestsDbContext>
    {
        public ResourceRequestsDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<ResourceRequestsDbContext>().UseInMemoryDatabase(storeName).Options);
    }

    /// <summary>Records the mail kinds in call order and captures the PI token the flow mints.</summary>
    private sealed class RecordingNotifications : INotificationSender
    {
        public List<string> Calls { get; } = [];
        public string? CapturedToken { get; private set; }

        public Task SendPiApprovalRequestedAsync(Email piEmail, string requestSlug, string actionToken, ProjectShortName project, ResourceName resource, CancellationToken ct)
        {
            CapturedToken = actionToken;
            Calls.Add("PiApprovalRequested");
            return Task.CompletedTask;
        }

        public Task SendRequesterAssignedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, CancellationToken ct)
        {
            Calls.Add("RequesterAssigned");
            return Task.CompletedTask;
        }

        public Task SendRequesterRejectedAsync(Email requesterEmail, string requestSlug, ProjectShortName project, ResourceName resource, string? reason, CancellationToken ct)
        {
            Calls.Add("RequesterRejected");
            return Task.CompletedTask;
        }

        public Task SendProviderResourceNeededAsync(Email providerSupportEmail, string requestSlug, ProjectShortName project, ResourceName resource, Email requesterContact, CancellationToken ct)
        {
            Calls.Add("ProviderResourceNeeded");
            return Task.CompletedTask;
        }

        // Unused self-onboarding kinds.
        public Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestCreatedToRequesterAsync(Email requesterEmail, Guid requestId, string actionToken, ResourceName resourceName, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestCreatedToOwnerAsync(Email ownerEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestAcceptedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestRejectedToRequesterAsync(Email requesterEmail, Guid requestId, ProjectShortName project, CancellationToken ct) => Task.CompletedTask;
        public Task SendResourcePublishedAsync(Email recipientEmail, string resourceName, string projectShortname, CancellationToken ct) => Task.CompletedTask;
        public Task SendRequestToCreateAccountAsync(Email recipientEmail, string resourceName, List<(string, string, string, string)> clusters, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public required ServiceProvider Services { get; init; }
        public required IProjectResourceRequestQuery Query { get; init; }
        public required IResourceRequestProjectionStore Store { get; init; }
        public required RecordingNotifications Notifications { get; init; }
        public required ProjectAggregate Project { get; init; }
        public required FakeRepository<ResourceAggregate, ProjectResourceKey> Resources { get; init; }

        /// <summary>Creates the requested resource the way the provider would — raising the activation event.</summary>
        public ResourceAggregate CreateTargetResource()
        {
            var provider = Core.Domain.Provider.Provider.Create(
                ProviderName.From(Provider), countryId: 1, supportEmail: Email.Create("support@acme.org"));
            var resource = ResourceAggregate.Create(ResourceName.Create(Resource), provider, UserId.From(Guid.NewGuid()), Now, Now.AddYears(1));
            Resources.Item = resource;
            return resource;
        }
    }

    private static async Task<Harness> BuildAsync(bool resourceExists, CancellationToken ct)
    {
        var factory = new InMemoryFactory(Guid.NewGuid().ToString());
        var store = new ResourceRequestProjectionStore(factory);
        var query = new ProjectResourceRequestQuery(factory);
        await store.InsertAsync(SeedRow(), ct);

        var providerAggregate = Core.Domain.Provider.Provider.Create(
            ProviderName.From(Provider), countryId: 1, supportEmail: Email.Create("support@acme.org"));
        var resources = new FakeRepository<ResourceAggregate, ProjectResourceKey>(
            resourceExists
                ? ResourceAggregate.Create(ResourceName.Create(Resource), providerAggregate, UserId.From(Guid.NewGuid()), Now, Now.AddYears(1))
                : null);

        var project = ProjectAggregate.Create(
            ProjectShortName.Create("proj"), ProjectName.Create("Project"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            Now, Now.AddYears(1), ServiceName.Default);

        var users = new Mock<IUserInfoQuery>();
        users.Setup(u => u.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetails(IdpId.Create("idp-1"), Email.Create("requester@example.org"), UserName.Create("requester")));

        var notifications = new RecordingNotifications();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestRepository, InMemoryWorkflowRepository>();
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<AssignResourceFlow>();

        services.AddSingleton<IResourceRequestProjectionStore>(store);
        services.AddSingleton<IProjectResourceRequestQuery>(query);
        services.AddSingleton<IOptions<ResourceRequestActionLinkOptions>>(
            Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 }));
        services.AddSingleton<INotificationSender>(notifications);
        services.AddSingleton(users.Object);
        services.AddSingleton<IRepository<ProjectAggregate, ProjectShortName>>(new FakeRepository<ProjectAggregate, ProjectShortName>(project));
        services.AddSingleton<IRepository<ResourceAggregate, ProjectResourceKey>>(resources);
        services.AddSingleton<IProviderRepository>(new FakeProviderRepository(providerAggregate));
        services.AddSingleton(new Mock<IUnitOfWork>().Object);

        return new Harness
        {
            Services = services.BuildServiceProvider(),
            Query = query,
            Store = store,
            Notifications = notifications,
            Project = project,
            Resources = resources,
        };
    }

    private static ProjectResourceRequest SeedRow() => new()
    {
        Slug = Slug,
        ProjectShortName = "proj",
        ProviderName = Provider,
        ResourceName = Resource,
        RequesterIdpId = "idp-1",
        ContactEmail = "pi@example.org",
        State = ResourceRequestState.PendingPiApproval,
        WorkflowRequestId = Guid.Empty,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact(DisplayName = "Full journey: missing resource → approve → park → activation event → resume → attach → Assigned")]
    public async Task Should_CompleteFullParkedJourney_When_ResumedViaActivationEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: false, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            // Start step ran: token minted + hash persisted to the real projection row (B04 → B02).
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            var parked = await h.Store.GetBySlugAsync(Slug, ct);
            Assert.NotNull(parked);
            Assert.Equal(ResourceRequestState.PendingPiApproval, parked.State);
            Assert.NotNull(parked.TokenHash);
            Assert.Equal(id, parked.WorkflowRequestId);

            // Cross-cutting: the minted plaintext token resolves back through the real B06 resolver over the
            // real query (mint → hash → find → verify roundtrip, in-memory DB).
            var token = h.Notifications.CapturedToken;
            Assert.NotNull(token);
            var resolved = await h.Query.FindByActionTokenAsync(token, ct);
            Assert.NotNull(resolved);
            Assert.Equal(Slug, resolved.Slug);
            Assert.True(ResourceRequestToken.Verify(token, parked.TokenHash, parked.TokenExpiresAt, DateTimeOffset.UtcNow));

            // PI approves → the resource does not exist yet → flow parks on the second gate; projection
            // advertises awaiting.
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForGateAsync(repo, id, "ResourceAvailability");
            Assert.Equal(ResourceRequestState.AwaitingResourceCreation, (await h.Store.GetBySlugAsync(Slug, ct))!.State);

            // The provider creates the awaited resource, then the REAL activation handler runs off the raised
            // event — it must find the awaiting row through the real query and resume the parked gate.
            var created = h.CreateTargetResource();
            var activated = Assert.IsType<ProjectResourceActivatedEvent>(
                Assert.Single(created.DomainEvents, e => e is ProjectResourceActivatedEvent));
            var handler = new ProjectResourceActivatedEventHandler(
                h.Query, h.Services.GetRequiredService<IWorkflowClient>(), NullLogger<ProjectResourceActivatedEventHandler>.Instance);
            await handler.HandleAsync(activated, ct);

            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);

            // Projection terminal, F5 link landed on the project aggregate, notifications in order.
            Assert.Equal(ResourceRequestState.Assigned, (await h.Store.GetBySlugAsync(Slug, ct))!.State);
            Assert.Single(h.Project.Resources);
            Assert.Equal(
                ["PiApprovalRequested", "ProviderResourceNeeded", "RequesterAssigned"],
                h.Notifications.Calls);
        });
    }

    [Fact(DisplayName = "Full journey: active resource → approve → attach immediately → Assigned")]
    public async Task Should_AttachImmediately_When_ApprovedAndResourceActive()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: true, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);

            Assert.Equal(ResourceRequestState.Assigned, (await h.Store.GetBySlugAsync(Slug, ct))!.State);
            Assert.Single(h.Project.Resources);
            Assert.Equal(["PiApprovalRequested", "RequesterAssigned"], h.Notifications.Calls);
        });
    }

    [Fact(DisplayName = "Reject journey: PI rejects → Rejected with reason, requester notified")]
    public async Task Should_Reject_When_PiRejects()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: true, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Reject, "not this quarter"));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);

            var row = await h.Store.GetBySlugAsync(Slug, ct);
            Assert.Equal(ResourceRequestState.Rejected, row!.State);
            Assert.Equal("not this quarter", row.Reason);
            Assert.Empty(h.Project.Resources);
            Assert.Contains("RequesterRejected", h.Notifications.Calls);
        });
    }

    [Fact(DisplayName = "Cancel journey: cancel at PI gate clears the token so it no longer resolves")]
    public async Task Should_CancelAndInvalidateToken_When_CancelledAtPiGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: true, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            var token = h.Notifications.CapturedToken!;
            Assert.NotNull(await h.Query.FindByActionTokenAsync(token, ct));

            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Cancel));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);

            var row = await h.Store.GetBySlugAsync(Slug, ct);
            Assert.Equal(ResourceRequestState.Cancelled, row!.State);
            Assert.Null(row.TokenHash);
            // Token no longer resolves through the real resolver once the links are invalidated (ADR-0064).
            Assert.Null(await h.Query.FindByActionTokenAsync(token, ct));
        });
    }

    [Fact(DisplayName = "Cancel journey: cancel at the resource-availability gate settles Cancelled")]
    public async Task Should_SettleCancelled_When_CancelledAtResourceAvailabilityGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: false, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForGateAsync(repo, id, "ResourceAvailability");
            Assert.Equal(ResourceRequestState.AwaitingResourceCreation, (await h.Store.GetBySlugAsync(Slug, ct))!.State);

            await client.ResumeAsync(id, new ResourceAvailabilitySignal(ResourceAvailabilityKind.Cancel));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);

            var row = await h.Store.GetBySlugAsync(Slug, ct);
            Assert.Equal(ResourceRequestState.Cancelled, row!.State);
            Assert.Null(row.TokenHash);
        });
    }

    [Fact(DisplayName = "Resend journey: rotates the PI token — old token stops resolving, new token approves to Assigned")]
    public async Task Should_RotateToken_When_ResendApprovalThenApproveWithNewToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: true, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            var firstToken = h.Notifications.CapturedToken!;
            Assert.NotNull(await h.Query.FindByActionTokenAsync(firstToken, ct));

            // Resend through the REAL endpoint handler (route 6): mints a fresh token and overwrites the stored
            // hash, which invalidates the previously emailed link (single-use rotation, ADR-0064).
            var options = Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 });
            var resend = await ResendApprovalEmailEndpoint.HandleAsync(
                new ResendApprovalEmailEndpoint.Request("proj", Slug), InScope(), h.Query, h.Store, h.Notifications, options,
                new RecordingAuditLogger<ResendApprovalEmailEndpoint.AuditCategory>(), ct);
            Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(resend).StatusCode);

            var secondToken = h.Notifications.CapturedToken!;
            Assert.NotEqual(firstToken, secondToken);

            // Old link is dead; the new link resolves to the same request — verified over the REAL B06 resolver
            // path against the real query/DB.
            Assert.Null(await h.Query.FindByActionTokenAsync(firstToken, ct));
            var resolved = await h.Query.FindByActionTokenAsync(secondToken, ct);
            Assert.NotNull(resolved);
            Assert.Equal(Slug, resolved.Slug);

            // Approving with the NEW token drives the real public action endpoint → gate resume → completion.
            var approve = await HandleResourceRequestActionEndpoint.HandleAsync(
                new HandleResourceRequestActionEndpoint.Request(secondToken, new HandleResourceRequestActionEndpoint.Body("Approve")),
                h.Query, client, Mock.Of<IAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>>(), ct);
            Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(approve).StatusCode);

            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Assigned, (await h.Store.GetBySlugAsync(Slug, ct))!.State);
            Assert.Single(h.Project.Resources);
        });
    }

    [Fact(DisplayName = "List projection: driven journey rows read back with correct states/slugs via the real query")]
    public async Task Should_ListProjectionRows_When_ReadBackAfterDrivingJourney()
    {
        var ct = TestContext.Current.CancellationToken;
        var h = await BuildAsync(resourceExists: true, ct);

        // A second, independent request on a different resource that stays PendingPiApproval.
        await h.Store.InsertAsync(new ProjectResourceRequest
        {
            Slug = "proj-fpga",
            ProjectShortName = "proj",
            ProviderName = Provider,
            ResourceName = "fpga",
            RequesterIdpId = "idp-1",
            ContactEmail = "pi@example.org",
            State = ResourceRequestState.PendingPiApproval,
            WorkflowRequestId = Guid.CreateVersion7(),
            CreatedAt = Now,
            UpdatedAt = Now,
        }, ct);

        await RunAsync(h, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Assigned, (await h.Store.GetBySlugAsync(Slug, ct))!.State);

            // Route-2 read path over the REAL query/DB: the unfiltered project list returns both driven rows with
            // their read-back states.
            var all = await h.Query.ListByProjectAsync("proj", state: null, page: 1, limit: 10, ct);
            Assert.Equal(2, all.TotalItems);
            Assert.Equal("Assigned", all.Items.Single(i => i.Slug == Slug).State);
            Assert.Equal("PendingPiApproval", all.Items.Single(i => i.Slug == "proj-fpga").State);

            // The state filter narrows to exactly the matching row.
            var assigned = await h.Query.ListByProjectAsync("proj", "Assigned", page: 1, limit: 10, ct);
            Assert.Equal(Slug, Assert.Single(assigned.Items).Slug);

            var pending = await h.Query.ListByProjectAsync("proj", "PendingPiApproval", page: 1, limit: 10, ct);
            Assert.Equal("proj-fpga", Assert.Single(pending.Items).Slug);
        });
    }

    private static Caller InScope() => new(
        IdpId.Create("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj") },
        });

    private static async Task RunAsync(Harness h, Func<IWorkflowClient, IRequestRepository, Guid, Task> body)
    {
        await using (h.Services)
        {
            var repository = h.Services.GetRequiredService<IRequestRepository>();
            var workers = h.Services.GetServices<IHostedService>().ToList();
            foreach (var worker in workers)
            {
                await worker.StartAsync(CancellationToken.None);
            }

            try
            {
                Guid id;
                using (var scope = h.Services.CreateScope())
                {
                    var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
                    id = await client.StartAsync<AssignResourceFlow>(new AssignResourceFlowInput(Slug));
                }

                using (var scope = h.Services.CreateScope())
                {
                    var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
                    await body(client, repository, id);
                }
            }
            finally
            {
                foreach (var worker in workers)
                {
                    await worker.StopAsync(CancellationToken.None);
                }
            }
        }
    }
}
