using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Primitives;
using Core.Domain.Project;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Domain.User;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.Flow;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
/// Engine e2e for the <see cref="AssignResourceFlow"/> per the <c>GatedFlowEndToEndTests</c> pattern: drives the
/// real worker + gates over an in-memory repository, with the Core ports faked. Covers Approve with the resource
/// present, Approve with no resource row at all (variant B's park) and its later resume, Reject, and Cancel and
/// Expire from each gate — asserting the projection lands on the right terminal state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AssignResourceFlowEndToEndTests
{
    private const string Slug = "proj-gpu";
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private static ProjectResourceRequest SeedRow() => new()
    {
        Slug = Slug,
        ProjectShortName = "proj",
        ProviderName = "acme",
        ResourceName = "gpu",
        RequesterIdpId = "idp-1",
        ContactEmail = "pi@example.org",
        State = ResourceRequestState.PendingPiApproval,
        WorkflowRequestId = Guid.Empty,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static ResourceAggregate BuildResource()
    {
        var provider = Provider.Create(ProviderName.From("acme"), countryId: 1, supportEmail: Email.Create("support@acme.org"));
        return ResourceAggregate.Create(ResourceName.Create("gpu"), provider, UserId.From(Guid.NewGuid()), Now, Now.AddYears(1));
    }

    private static FakeRepository<ResourceAggregate, ProjectResourceKey> ExistingResource() => new(BuildResource());

    private static FakeRepository<ResourceAggregate, ProjectResourceKey> NoResource() => new(null);

    private static (ServiceProvider Provider, FakeProjectResourceRequestStore Store, Mock<INotificationSender> Notifications) Build(
        FakeRepository<ResourceAggregate, ProjectResourceKey> resources)
    {
        var store = new FakeProjectResourceRequestStore().Seed(SeedRow());
        var project = ProjectAggregate.Create(
            ProjectShortName.Create("proj"), ProjectName.Create("Project"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            Now, Now.AddYears(1), ServiceName.Default);
        var providerAggregate = Provider.Create(ProviderName.From("acme"), countryId: 1, supportEmail: Email.Create("support@acme.org"));

        var users = new Mock<IUserInfoQuery>();
        users.Setup(u => u.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetails(IdpId.Create("idp-1"), Email.Create("requester@example.org"), UserName.Create("requester")));

        var notifications = new Mock<INotificationSender>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestRepository, InMemoryWorkflowRepository>();
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<AssignResourceFlow>();

        services.AddSingleton<IResourceRequestProjectionStore>(store);
        services.AddSingleton<IOptions<ResourceRequestActionLinkOptions>>(
            Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 }));
        services.AddSingleton(notifications.Object);
        services.AddSingleton(users.Object);
        services.AddSingleton<IRepository<ProjectAggregate, ProjectShortName>>(new FakeRepository<ProjectAggregate, ProjectShortName>(project));
        services.AddSingleton<IRepository<ResourceAggregate, ProjectResourceKey>>(resources);
        services.AddSingleton<IProviderRepository>(new FakeProviderRepository(providerAggregate));
        services.AddSingleton(new Mock<IUnitOfWork>().Object);

        return (services.BuildServiceProvider(), store, notifications);
    }

    [Fact(DisplayName = "Approve + existing resource → attach → Assigned")]
    public async Task Should_Assign_When_ApprovedAndResourceExists()
    {
        var (provider, store, _) = Build(ExistingResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Assigned, store.Row(Slug).State);
        });
    }

    [Fact(DisplayName = "Approve + missing resource → park → the resource appears → resume → Assigned")]
    public async Task Should_ParkThenAssign_When_ApprovedResourceIsCreatedWhileParked()
    {
        var resources = NoResource();
        var (provider, store, _) = Build(resources);
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));

            await FlowEngineWaits.WaitForGateAsync(repo, id, "ResourceAvailability");
            Assert.Equal(ResourceRequestState.AwaitingResourceCreation, store.Row(Slug).State);

            resources.Item = BuildResource(); // the provider creates the awaited resource
            await client.ResumeAsync(id, new ResourceAvailabilitySignal(ResourceAvailabilityKind.Available));

            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Assigned, store.Row(Slug).State);
        });
    }

    [Fact(DisplayName = "Approve + no resource row at all → provider support notified → park at ResourceAvailability")]
    public async Task Should_NotifyProviderSupportAndPark_When_ApprovedAndResourceDoesNotExist()
    {
        var (provider, store, notifications) = Build(NoResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));

            await FlowEngineWaits.WaitForGateAsync(repo, id, "ResourceAvailability");
            Assert.Equal(ResourceRequestState.AwaitingResourceCreation, store.Row(Slug).State);
            notifications.Verify(n => n.SendProviderResourceNeededAsync(
                Email.Create("support@acme.org"), Slug, It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(),
                It.IsAny<Email>(), It.IsAny<CancellationToken>()),
                Times.Once);
        });
    }

    [Fact(DisplayName = "Reject → Rejected with reason")]
    public async Task Should_Reject_When_PiRejects()
    {
        var (provider, store, _) = Build(ExistingResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Reject, "not this quarter"));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Rejected, store.Row(Slug).State);
            Assert.Equal("not this quarter", store.Row(Slug).Reason);
        });
    }

    [Fact(DisplayName = "Cancel at PiApproval gate → Cancelled")]
    public async Task Should_Cancel_When_CancelledAtPiApprovalGate()
    {
        var (provider, store, _) = Build(ExistingResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Cancel));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Cancelled, store.Row(Slug).State);
            Assert.Null(store.Row(Slug).TokenHash);
        });
    }

    [Fact(DisplayName = "Cancel at ResourceAvailability gate → Cancelled")]
    public async Task Should_Cancel_When_CancelledAtResourceAvailabilityGate()
    {
        var (provider, store, _) = Build(NoResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForGateAsync(repo, id, "ResourceAvailability");
            await client.ResumeAsync(id, new ResourceAvailabilitySignal(ResourceAvailabilityKind.Cancel));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Cancelled, store.Row(Slug).State);
        });
    }

    [Fact(DisplayName = "Expire at PiApproval gate → Expired")]
    public async Task Should_Expire_When_ExpiredAtPiApprovalGate()
    {
        var (provider, store, _) = Build(ExistingResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Expire));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Expired, store.Row(Slug).State);
        });
    }

    [Fact(DisplayName = "Expire at ResourceAvailability gate → Expired")]
    public async Task Should_Expire_When_ExpiredAtResourceAvailabilityGate()
    {
        var (provider, store, _) = Build(NoResource());
        await RunAsync(provider, async (client, repo, id) =>
        {
            await FlowEngineWaits.WaitForGateAsync(repo, id, "PiApproval");
            await client.ResumeAsync(id, new PiDecision(PiDecisionKind.Approve));
            await FlowEngineWaits.WaitForGateAsync(repo, id, "ResourceAvailability");
            await client.ResumeAsync(id, new ResourceAvailabilitySignal(ResourceAvailabilityKind.Expire));
            await FlowEngineWaits.WaitForStatusAsync(repo, id, RequestStatus.Completed);
            Assert.Equal(ResourceRequestState.Expired, store.Row(Slug).State);
        });
    }

    private static async Task RunAsync(ServiceProvider provider, Func<IWorkflowClient, IRequestRepository, Guid, Task> body)
    {
        await using (provider)
        {
            var repository = provider.GetRequiredService<IRequestRepository>();
            var workers = provider.GetServices<IHostedService>().ToList();
            foreach (var worker in workers)
            {
                await worker.StartAsync(CancellationToken.None);
            }

            try
            {
                Guid id;
                using (var scope = provider.CreateScope())
                {
                    var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
                    id = await client.StartAsync<AssignResourceFlow>(new AssignResourceFlowInput(Slug));
                }

                using (var scope = provider.CreateScope())
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
