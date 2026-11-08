using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.ResendApprovalEmail;
using Core.UnitTests.ResourceRequests.Flow;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Unit tests for <see cref="ResendApprovalEmailEndpoint"/> (route 6, ADR-0064): resend mints a new token
/// (hash differs, expiry extended) and re-emails the PI without touching the engine; resend outside
/// <c>PendingPiApproval</c> → 409; anti-leak 404 discipline preserved.
/// </summary>
public sealed class ResendApprovalEmailEndpointTests
{
    private const string Project = "proj";
    private const string Slug = "proj-gpu";

    private static Caller InScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create(Project) },
        });

    private static Caller OutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ResourceRequestDetail Detail(ResourceRequestState state, string project = Project) => new(
        Slug, state.Value, project, "acme", "gpu", "idp-1", "pi@example.org", Guid.CreateVersion7(), null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static ProjectResourceRequest Row(string? tokenHash) => new()
    {
        Slug = Slug,
        ProjectShortName = Project,
        ProviderName = "acme",
        ResourceName = "gpu",
        RequesterIdpId = "idp-1",
        ContactEmail = "pi@example.org",
        State = ResourceRequestState.PendingPiApproval,
        TokenHash = tokenHash,
        TokenExpiresAt = tokenHash is null ? null : DateTimeOffset.UtcNow.AddHours(1),
        WorkflowRequestId = Guid.CreateVersion7(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<(IResult Result, FakeProjectResourceRequestStore Store, Mock<INotificationSender> Notifications, string? SentToken)> InvokeAsync(
        Caller caller, ResourceRequestDetail? detail, ProjectResourceRequest? seed = null)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(detail);

        var store = new FakeProjectResourceRequestStore();
        if (seed is not null)
            store.Seed(seed);

        string? sentToken = null;
        var notifications = new Mock<INotificationSender>();
        notifications
            .Setup(n => n.SendPiApprovalRequestedAsync(It.IsAny<Email>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .Callback((Email _, string _, string token, ProjectShortName _, ResourceName _, CancellationToken _) => sentToken = token)
            .Returns(Task.CompletedTask);

        var options = Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 });

        var result = await ResendApprovalEmailEndpoint.HandleAsync(
            new ResendApprovalEmailEndpoint.Request(Project, Slug), caller, query.Object, store, notifications.Object,
            options, new RecordingAuditLogger<ResendApprovalEmailEndpoint.AuditCategory>(), CancellationToken.None);
        return (result, store, notifications, sentToken);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_MintNewTokenExtendExpiryAndEmailPi_When_PendingPiApproval()
    {
        var oldHash = ResourceRequestToken.Hash(ResourceRequestToken.Generate());
        var seed = Row(oldHash);
        seed.TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);

        var (result, store, notifications, sentToken) = await InvokeAsync(
            InScope(), Detail(ResourceRequestState.PendingPiApproval), seed);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var row = store.Row(Slug);
        Assert.NotNull(sentToken);
        Assert.NotNull(row.TokenHash);
        Assert.NotEqual(oldHash, row.TokenHash);                              // old token invalidated
        Assert.Equal(ResourceRequestToken.Hash(sentToken!), row.TokenHash);   // stored hash matches emailed token
        Assert.True(row.TokenExpiresAt > DateTimeOffset.UtcNow.AddHours(100)); // expiry extended
        notifications.Verify(n => n.SendPiApprovalRequestedAsync(
            It.IsAny<Email>(), Slug, sentToken!, It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("AwaitingResourceCreation")]
    [InlineData("Assigned")]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    public async Task Should_ReturnConflict_When_NotPending(string state)
    {
        var (result, _, notifications, _) = await InvokeAsync(
            InScope(), Detail(ResourceRequestState.From(state)), Row("abc"));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("resource_request.not_resendable", problem.ProblemDetails.Extensions["code"]);
        notifications.Verify(n => n.SendPiApprovalRequestedAsync(
            It.IsAny<Email>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_MissingOutOfScopeOrWrongProject()
    {
        var (missing, _, _, _) = await InvokeAsync(InScope(), detail: null);
        var (outOfScope, _, _, _) = await InvokeAsync(OutOfScope(), Detail(ResourceRequestState.PendingPiApproval));
        var (wrongProject, _, _, _) = await InvokeAsync(InScope(), Detail(ResourceRequestState.PendingPiApproval, project: "other"));

        var notThere = Assert.IsType<ProblemHttpResult>(missing);
        var hidden = Assert.IsType<ProblemHttpResult>(outOfScope);
        var mismatched = Assert.IsType<ProblemHttpResult>(wrongProject);

        Assert.Equal(StatusCodes.Status404NotFound, notThere.StatusCode);
        foreach (var other in new[] { hidden, mismatched })
        {
            Assert.Equal(notThere.StatusCode, other.StatusCode);
            Assert.Equal(notThere.ProblemDetails.Extensions["code"], other.ProblemDetails.Extensions["code"]);
            Assert.Equal(notThere.ProblemDetails.Detail, other.ProblemDetails.Detail);
        }
    }
}
