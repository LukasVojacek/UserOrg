using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.AcceptOnboardingRequest;
using Core.Features.OnboardingRequest.RejectOnboardingRequest;
using Core.Features.Request.AcceptRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Audit;

public sealed class RequestDecisionEarlyExitAuditTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly IdpId RequesterIdpId = IdpId.Create("idp-req");
    private const string TheSlug = "req-req-proj-1-abc123";
    private const string UnparseableProject = " ";

    private static Caller CallerInScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create("OTHER-1") } });

    private static Mock<IOnboardingRequestQuery> OnboardingQuery()
    {
        var query = new Mock<IOnboardingRequestQuery>();
        query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingRequestSettlement(ShortName, RequesterIdpId, OnboardingRequestState.Pending));
        return query;
    }

    private static Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>> OnboardingRequests(bool present)
    {
        var request = OnboardingRequestAggregate.Create(
            ShortName, RequesterIdpId, Email.Create("req@example.com"), OnboardingRequestSlug.Create(TheSlug));
        var repository = new Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>>();
        repository.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(present ? request : null));
        return repository;
    }

    private static Mock<IUserDirectory> Directory(bool callerProvisioned)
    {
        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerProvisioned ? UserId.New() : (UserId?)null);
        return directory;
    }

    private static async Task<(IResult Result, RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory> Audit)> AcceptOnboardingAsync(
        string projectShortName, bool requestPresent = true, bool callerProvisioned = true, Caller? caller = null)
    {
        var audit = new RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory>();
        var result = await AcceptOnboardingRequestEndpoint.HandleAsync(
            new AcceptOnboardingRequestEndpoint.Request(projectShortName, TheSlug),
            caller ?? CallerInScope(), OnboardingQuery().Object, OnboardingRequests(requestPresent).Object,
            new Mock<IRepository<ProjectAggregate, ProjectShortName>>().Object,
            new Mock<IRepository<Domain.User.User, IdpId>>().Object, Directory(callerProvisioned).Object,
            new Mock<IRoleListService>().Object, Options.Create(new WellKnownRoles()),
            new Mock<INotificationSender>().Object, new Mock<IUnitOfWork>().Object,
            new Mock<IOutputCacheInvalidator>().Object, audit, new DefaultHttpContext(), CancellationToken.None);
        return (result, audit);
    }

    private static async Task<(IResult Result, RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory> Audit)> RejectOnboardingAsync(
        string projectShortName, bool requestPresent = true, Caller? caller = null)
    {
        var audit = new RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory>();
        var result = await RejectOnboardingRequestEndpoint.HandleAsync(
            new RejectOnboardingRequestEndpoint.Request(projectShortName, TheSlug),
            caller ?? CallerInScope(), OnboardingQuery().Object, OnboardingRequests(requestPresent).Object,
            new Mock<INotificationSender>().Object, new Mock<IUnitOfWork>().Object, audit, CancellationToken.None);
        return (result, audit);
    }

    private static async Task<(IResult Result, RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory> Audit)> AcceptRequestAsync(
        bool requestPresent = true, bool callerProvisioned = true)
    {
        var requestQuery = new Mock<IRequestQuery>();
        requestQuery.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var request = RequestAggregate.Create(Email.Create("req@example.com"), ProjectId.New(), UserId.New(), RequestType.SelfOnboarding);
        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(requestPresent ? request : null));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShortName);

        var audit = new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>();
        var result = await AcceptRequestEndpoint.HandleAsync(
            new AcceptRequestEndpoint.Request(TheSlug),
            CallerInScope(), requestQuery.Object, requests.Object,
            new Mock<IRepository<ProjectAggregate, ProjectShortName>>().Object,
            new Mock<IRepository<Domain.User.User, IdpId>>().Object, Directory(callerProvisioned).Object,
            projectDirectory.Object, new Mock<IRoleListService>().Object, Options.Create(new WellKnownRoles()),
            new Mock<INotificationSender>().Object, new Mock<IUnitOfWork>().Object,
            new Mock<IOutputCacheInvalidator>().Object, audit, new DefaultHttpContext(), CancellationToken.None);
        return (result, audit);
    }

    private static string? CodeOf(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        return problem.ProblemDetails.Extensions["code"]?.ToString();
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheAcceptedOnboardingRoutesProjectIsUnparseable()
    {
        var (result, audit) = await AcceptOnboardingAsync(UnparseableProject);

        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheAcceptedOnboardingRequestVanishedAfterResolving()
    {
        var (result, audit) = await AcceptOnboardingAsync(ShortName.Value, requestPresent: false);

        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheOnboardingAcceptingCallerIsNotProvisioned()
    {
        var (result, audit) = await AcceptOnboardingAsync(ShortName.Value, callerProvisioned: false);

        Assert.NotEqual(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_AuditTheAcceptActionAsDenied_When_TheOnboardingAcceptingCallerIsOutOfScope()
    {
        var (result, audit) = await AcceptOnboardingAsync(ShortName.Value, caller: CallerOutOfScope());

        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        var denied = Assert.Single(audit.Events);
        Assert.Equal("onboarding_request.self.accepted", denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal("project.caller_out_of_scope", denied.Reason);
        Assert.Equal(TheSlug, denied.TargetId);
    }

    [Fact]
    public async Task Should_AuditTheRejectActionAsDenied_When_TheOnboardingRejectingCallerIsOutOfScope()
    {
        var (result, audit) = await RejectOnboardingAsync(ShortName.Value, caller: CallerOutOfScope());

        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        var denied = Assert.Single(audit.Events);
        Assert.Equal("onboarding_request.self.rejected", denied.Action);
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal("project.caller_out_of_scope", denied.Reason);
        Assert.Equal(TheSlug, denied.TargetId);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheRejectedOnboardingRoutesProjectIsUnparseable()
    {
        var (result, audit) = await RejectOnboardingAsync(UnparseableProject);

        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheRejectedOnboardingRequestVanishedAfterResolving()
    {
        var (result, audit) = await RejectOnboardingAsync(ShortName.Value, requestPresent: false);

        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheAcceptedRequestVanishedAfterItsSlugResolved()
    {
        var (result, audit) = await AcceptRequestAsync(requestPresent: false);

        Assert.Equal("request.not_found", CodeOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheRequestAcceptingCallerIsNotProvisioned()
    {
        var (result, audit) = await AcceptRequestAsync(callerProvisioned: false);

        Assert.NotEqual("request.not_found", CodeOf(result));
        Assert.Empty(audit.Events);
    }
}
