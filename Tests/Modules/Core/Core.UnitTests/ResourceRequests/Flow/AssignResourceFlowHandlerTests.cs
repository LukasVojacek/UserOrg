using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Primitives;
using Core.Domain.Project;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Domain.User;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.Flow.Handlers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Moq;

using WorkflowEngine.Contracts.Exceptions;

using ProjectAggregate = Core.Domain.Project.Project;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>
/// Unit tests for the assign-resource flow step handlers with mocked ports (ADR-0063/0064): token
/// hash-at-rest on the projection, the resource-exists branch signal, F5 reuse + idempotency on attach, terminal
/// state writes, and the transient-vs-permanent failure contract.
/// </summary>
public sealed class AssignResourceFlowHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    private static ProjectResourceRequest Row(
        ResourceRequestState? state = null, string? tokenHash = null) => new()
        {
            Slug = "proj-gpu",
            ProjectShortName = "proj",
            ProviderName = "acme",
            ResourceName = "gpu",
            RequesterIdpId = "idp-1",
            ContactEmail = "pi@example.org",
            State = state ?? ResourceRequestState.PendingPiApproval,
            TokenHash = tokenHash,
            TokenExpiresAt = tokenHash is null ? null : Now.AddHours(1),
            WorkflowRequestId = Guid.Empty,
            Reason = null,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static ResourceAggregate Resource()
    {
        var provider = Provider.Create(ProviderName.From("acme"), countryId: 1);
        return ResourceAggregate.Create(ResourceName.Create("gpu"), provider, UserId.From(Guid.NewGuid()), Now, Now.AddYears(1));
    }

    private static ProjectAggregate Project() => ProjectAggregate.Create(
        ProjectShortName.Create("proj"),
        ProjectName.Create("Project"),
        ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
        UserId.New(),
        Now,
        Now.AddYears(1),
        ServiceName.Default);

    private static IOptions<ResourceRequestActionLinkOptions> LinkOptions() =>
        Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 });

    /// <summary>Decorates a real store to force <see cref="IResourceRequestProjectionStore.UpdateBySlugAsync"/>
    /// to fail, so the handler's transient-vs-permanent classification of that call can be tested in isolation.</summary>
    private sealed class UpdateThrowingStore(IResourceRequestProjectionStore inner, Func<Exception> failWith)
        : IResourceRequestProjectionStore
    {
        public Task<ProjectResourceRequest?> GetBySlugAsync(string slug, CancellationToken ct) => inner.GetBySlugAsync(slug, ct);

        public Task InsertAsync(ProjectResourceRequest row, CancellationToken ct) => inner.InsertAsync(row, ct);

        public Task DeleteBySlugAsync(string slug, CancellationToken ct) => inner.DeleteBySlugAsync(slug, ct);

        public Task UpdateBySlugAsync(string slug, Action<ProjectResourceRequest> mutate, CancellationToken ct) => throw failWith();
    }

    [Fact]
    public async Task Should_StoreTokenHashAndEmailPi_When_SendPiApprovalEmailRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row(ResourceRequestState.PendingPiApproval));
        var notifications = new Mock<INotificationSender>();
        string? sentToken = null;
        notifications
            .Setup(n => n.SendPiApprovalRequestedAsync(It.IsAny<Email>(), "proj-gpu", It.IsAny<string>(), It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .Callback((Email _, string _, string token, ProjectShortName _, ResourceName _, CancellationToken _) => sentToken = token)
            .Returns(Task.CompletedTask);

        var handler = new SendPiApprovalEmailHandler(store, notifications.Object, LinkOptions());
        var requestId = Guid.CreateVersion7();

        await handler.HandleAsync(new SendPiApprovalEmailStep(requestId, "proj-gpu"), ct);

        var row = store.Row("proj-gpu");
        Assert.NotNull(sentToken);
        Assert.NotNull(row.TokenHash);
        Assert.NotEqual(sentToken, row.TokenHash); // hash-at-rest: plaintext never stored
        Assert.Equal(ResourceRequestToken.Hash(sentToken!), row.TokenHash);
        Assert.NotNull(row.TokenExpiresAt);
        Assert.Equal(ResourceRequestState.PendingPiApproval, row.State);
        Assert.Equal(requestId, row.WorkflowRequestId);
        notifications.VerifyAll();
    }

    [Fact]
    public async Task Should_ThrowRetryable_When_PiEmailFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row());
        var notifications = new Mock<INotificationSender>();
        notifications
            .Setup(n => n.SendPiApprovalRequestedAsync(It.IsAny<Email>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("smtp down"));

        var handler = new SendPiApprovalEmailHandler(store, notifications.Object, LinkOptions());

        await Assert.ThrowsAsync<RetryableException>(
            () => handler.HandleAsync(new SendPiApprovalEmailStep(Guid.NewGuid(), "proj-gpu"), ct));
    }

    [Fact]
    public async Task Should_ReportAssignableAndInvalidateUsedToken_When_CheckResourceExistsRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = ResourceRequestToken.Generate();
        var store = new FakeProjectResourceRequestStore().Seed(Row(tokenHash: ResourceRequestToken.Hash(token)));
        var resources = new FakeRepository<ResourceAggregate, ProjectResourceKey>(Resource());
        var handler = new CheckResourceExistsHandler(store, resources);

        var result = await handler.HandleAsync(new CheckResourceExistsStep(Guid.NewGuid(), "proj-gpu"), ct);

        Assert.True(CheckResourceExistsResult.HasResource(result));

        // Approve path settles the PI decision → single-use token invalidated; it can never verify again.
        var row = store.Row("proj-gpu");
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
        Assert.False(ResourceRequestToken.Verify(token, row.TokenHash, row.TokenExpiresAt, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Should_ReportMissingAndInvalidateUsedToken_When_ResourceDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = ResourceRequestToken.Generate();
        var store = new FakeProjectResourceRequestStore().Seed(Row(tokenHash: ResourceRequestToken.Hash(token)));
        var resources = new FakeRepository<ResourceAggregate, ProjectResourceKey>(item: null);
        var handler = new CheckResourceExistsHandler(store, resources);

        var result = await handler.HandleAsync(new CheckResourceExistsStep(Guid.NewGuid(), "proj-gpu"), ct);

        // Variant B: a free-text resource name that has no catalog row yet is the normal case, not a failure.
        Assert.False(CheckResourceExistsResult.HasResource(result));
        Assert.Null(store.Row("proj-gpu").TokenHash);
    }

    [Fact]
    public async Task Should_ReuseF5PathAndSetAssigned_When_AttachResourceRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row());
        var project = Project();
        var resource = Resource();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AttachResourceHandler(
            store,
            new FakeRepository<ProjectAggregate, ProjectShortName>(project),
            new FakeRepository<ResourceAggregate, ProjectResourceKey>(resource),
            uow.Object);

        await handler.HandleAsync(new AttachResourceStep(Guid.NewGuid(), "proj-gpu"), ct);

        Assert.Contains(project.Resources, r => r.Name == resource.ResourceName);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ResourceRequestState.Assigned, store.Row("proj-gpu").State);
    }

    [Fact]
    public async Task Should_BeIdempotent_When_AttachResourceLinkAlreadyActive()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row());
        var project = Project();
        var resource = Resource();
        Assert.True(project.AssignResource(resource).IsSuccess); // pre-existing active link

        var uow = new Mock<IUnitOfWork>();
        var handler = new AttachResourceHandler(
            store,
            new FakeRepository<ProjectAggregate, ProjectShortName>(project),
            new FakeRepository<ResourceAggregate, ProjectResourceKey>(resource),
            uow.Object);

        await handler.HandleAsync(new AttachResourceStep(Guid.NewGuid(), "proj-gpu"), ct);

        // Already-assigned is treated as success: no second save, projection still lands on Assigned.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(ResourceRequestState.Assigned, store.Row("proj-gpu").State);
    }

    [Fact]
    public async Task Should_ThrowRetryable_When_AssignmentSaveFailsTransiently()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row());
        var project = Project();
        var resource = Resource();
        var transientFailure = new DbUpdateException("deadlock");
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(transientFailure);

        var handler = new AttachResourceHandler(
            store,
            new FakeRepository<ProjectAggregate, ProjectShortName>(project),
            new FakeRepository<ResourceAggregate, ProjectResourceKey>(resource),
            uow.Object);

        var thrown = await Assert.ThrowsAsync<RetryableException>(
            () => handler.HandleAsync(new AttachResourceStep(Guid.NewGuid(), "proj-gpu"), ct));

        Assert.Same(transientFailure, thrown.InnerException);
        Assert.Equal("resource_request.assignment_save_failed", thrown.ErrorInfo.ErrorCode);
        // The handler must abort before the projection write rather than half-completing.
        Assert.NotEqual(ResourceRequestState.Assigned, store.Row("proj-gpu").State);
    }

    [Fact]
    public async Task Should_ThrowRetryable_When_ProjectionUpdateFailsTransiently()
    {
        var ct = TestContext.Current.CancellationToken;
        var transientFailure = new DbUpdateException("connection reset");
        var store = new UpdateThrowingStore(
            new FakeProjectResourceRequestStore().Seed(Row()),
            () => transientFailure);
        var project = Project();
        var resource = Resource();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AttachResourceHandler(
            store,
            new FakeRepository<ProjectAggregate, ProjectShortName>(project),
            new FakeRepository<ResourceAggregate, ProjectResourceKey>(resource),
            uow.Object);

        var thrown = await Assert.ThrowsAsync<RetryableException>(
            () => handler.HandleAsync(new AttachResourceStep(Guid.NewGuid(), "proj-gpu"), ct));

        Assert.Same(transientFailure, thrown.InnerException);
        Assert.Equal("resource_request.projection_update_failed", thrown.ErrorInfo.ErrorCode);
    }

    [Fact]
    public async Task Should_PropagatePermanently_When_ProjectionUpdateRowIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new UpdateThrowingStore(
            new FakeProjectResourceRequestStore().Seed(Row()),
            () => new ResourceRequestRowMissingException("proj-gpu"));
        var project = Project();
        var resource = Resource();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AttachResourceHandler(
            store,
            new FakeRepository<ProjectAggregate, ProjectShortName>(project),
            new FakeRepository<ResourceAggregate, ProjectResourceKey>(resource),
            uow.Object);

        // A missing projection row (a delete-race between resolving the slug and this write) is permanent, not
        // transient, so it must NOT be converted into a RetryableException — retrying would only burn
        // MaxStepRetries pointlessly.
        await Assert.ThrowsAsync<ResourceRequestRowMissingException>(
            () => handler.HandleAsync(new AttachResourceStep(Guid.NewGuid(), "proj-gpu"), ct));
    }

    [Fact]
    public async Task Should_ClearTokenAndCancel_When_InvalidateLinksRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row(ResourceRequestState.PendingPiApproval, tokenHash: "abc"));
        var handler = new InvalidateLinksHandler(store);

        await handler.HandleAsync(new InvalidateLinksStep(Guid.NewGuid(), "proj-gpu", "changed my mind"), ct);

        var row = store.Row("proj-gpu");
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
        Assert.Equal(ResourceRequestState.Cancelled, row.State);
        Assert.Equal("changed my mind", row.Reason);
    }

    [Fact]
    public async Task Should_ClearTokenAndExpire_When_InvalidateLinksRunsWithExpiredFlag()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FakeProjectResourceRequestStore().Seed(Row(ResourceRequestState.AwaitingResourceCreation, tokenHash: "abc"));
        var handler = new InvalidateLinksHandler(store);

        await handler.HandleAsync(new InvalidateLinksStep(Guid.NewGuid(), "proj-gpu", null, Expired: true), ct);

        var row = store.Row("proj-gpu");
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
        Assert.Equal(ResourceRequestState.Expired, row.State);
    }

    [Fact]
    public async Task Should_RejectWithReasonAndEmailRequester_When_NotifyRequesterRejectedRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = ResourceRequestToken.Generate();
        var store = new FakeProjectResourceRequestStore().Seed(Row(tokenHash: ResourceRequestToken.Hash(token)));
        var users = new Mock<IUserInfoQuery>();
        users.Setup(u => u.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetails(IdpId.Create("idp-1"), Email.Create("requester@example.org"), UserName.Create("requester")));
        var notifications = new Mock<INotificationSender>();

        var handler = new NotifyRequesterRejectedHandler(store, users.Object, notifications.Object);

        await handler.HandleAsync(new NotifyRequesterRejectedStep(Guid.NewGuid(), "proj-gpu", "not this quarter"), ct);

        var row = store.Row("proj-gpu");
        Assert.Equal(ResourceRequestState.Rejected, row.State);
        Assert.Equal("not this quarter", row.Reason);
        // Reject settles the PI decision → single-use token invalidated; it can never verify again.
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
        Assert.False(ResourceRequestToken.Verify(token, row.TokenHash, row.TokenExpiresAt, DateTimeOffset.UtcNow));
        notifications.Verify(n => n.SendRequesterRejectedAsync(
            It.IsAny<Email>(), "proj-gpu", It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), "not this quarter", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
