using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.CreateOnboardingRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Requests;

public sealed class CreateOnboardingRequestEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly IdpId CallerIdpId = IdpId.Create("idp-req");
    private static readonly Email RequesterEmail = Email.Create("req@example.com");
    private static readonly Email OwnerEmail = Email.Create("owner@example.com");
    private static readonly Guid ReadBackId = Guid.NewGuid();

    private sealed class Sut
    {
        public string RouteShortName { get; set; } = ShortName.Value;

        public ProjectAggregate? Project { get; set; } = BuildProject(OwnerEmail);

        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();
        public Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>> OnboardingRequests { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IUserDirectory> Directory { get; } = new();
        public Mock<IProjectDirectory> ProjectDirectory { get; } = new();
        public Mock<IOnboardingRequestQuery> Query { get; } = new();
        public Mock<INotificationSender> Notifications { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();

        public Sut()
        {
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Project));
            OnboardingRequests.Setup(r => r.AddAsync(It.IsAny<OnboardingRequestAggregate>(), It.IsAny<CancellationToken>()))
                .Returns<OnboardingRequestAggregate, CancellationToken>((r, _) => ValueTask.FromResult<OnboardingRequestAggregate?>(r));
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Domain.User.User?>(
                    Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), CallerIdpId)));
            Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
            ProjectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProjectId.New());
            Query.Setup(q => q.FindPendingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
            Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ReadBackId);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync() =>
            CreateOnboardingRequestEndpoint.HandleAsync(
                new CreateOnboardingRequestEndpoint.Request(RouteShortName),
                new Caller(CallerIdpId, "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>()),
                Projects.Object, OnboardingRequests.Object, Users.Object, Directory.Object, ProjectDirectory.Object,
                Query.Object, Notifications.Object, UnitOfWork.Object, Cache.Object, CancellationToken.None);

        public void VerifyNothingWritten()
        {
            OnboardingRequests.Verify(r => r.AddAsync(It.IsAny<OnboardingRequestAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Notifications.VerifyNoOtherCalls();
            Cache.VerifyNoOtherCalls();
        }
    }

    private static ProjectAggregate BuildProject(Email? email)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable,
            email: email);
        project.ClearDomainEvents();
        return project;
    }

    private static void AssertProblem(IResult result, int status, string code, string? detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_AddSaveNotifyBothAndEvict_When_TheRequestIsCreated()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        var created = Assert.IsType<Created<CreateOnboardingRequestEndpoint.Response>>(result);
        Assert.Equal($"v2/Projects/PROJ-1/OnboardingRequests/{created.Value!.Slug}", created.Location);
        sut.OnboardingRequests.Verify(r => r.AddAsync(
            It.Is<OnboardingRequestAggregate>(a => a.Slug == OnboardingRequestSlug.Create(created.Value.Slug) && a.ContactEmail == RequesterEmail),
            It.IsAny<CancellationToken>()), Times.Once);
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Query.Verify(q => q.FindIdBySlugAsync(created.Value.Slug, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestCreatedToRequesterAsync(RequesterEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestCreatedToOwnerAsync(OwnerEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotifyOnlyTheRequesterWithAnEmptyId_When_NoOwnerEmailAndNoReadBack()
    {
        var sut = new Sut { Project = BuildProject(email: null) };
        sut.Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await sut.InvokeAsync();

        Assert.IsType<Created<CreateOnboardingRequestEndpoint.Response>>(result);
        sut.Notifications.Verify(n => n.SendRequestCreatedToRequesterAsync(RequesterEmail, Guid.Empty, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnRequired_When_TheRouteShortNameIsInvalid()
    {
        var sut = new Sut { RouteShortName = "" };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status400BadRequest, "validation.required", "Project short name is required.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_TheCallerHasNoUserId()
    {
        var sut = new Sut();
        sut.Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserId?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned", "The authenticated caller is not a known user.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisionedWithoutDetail_When_TheCallerUserRowIsMissing()
    {
        var sut = new Sut();
        sut.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned", null);
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheProjectIdCannotBeResolved()
    {
        var sut = new Sut();
        sut.ProjectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProjectId?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, OnboardingRequestErrorCodes.NotFound, "Project not found.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnDuplicatePendingWithoutWriting_When_APendingRequestExists()
    {
        var sut = new Sut();
        sut.Query.Setup(q => q.FindPendingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, OnboardingRequestErrorCodes.DuplicatePendingRequest, "You already have a pending request for project 'PROJ-1'.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnAlreadyMemberBeforeResolvingTheProjectId_When_TheCallerIsAMember()
    {
        var sut = new Sut();
        var requester = Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), CallerIdpId);
        Assert.True(sut.Project!.AssignUser(requester, UserId.New()).IsSuccess);
        sut.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(requester));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, OnboardingRequestErrorCodes.AlreadyActiveMember, "You are already an active member of project 'PROJ-1'.");
        sut.ProjectDirectory.Verify(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.VerifyNothingWritten();
    }
}
