using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.CreateRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class CreateRequestEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Email RequesterEmail = Email.Create("req@example.com");
    private static readonly Email OwnerEmail = Email.Create("owner@example.com");
    private static readonly Guid ReadBackId = Guid.NewGuid();

    private sealed class Sut
    {
        public string ProjectShortNameInput { get; set; } = ShortName.Value;

        public ProjectAggregate? Project { get; set; } = BuildProject(OwnerEmail);

        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();
        public Mock<IRepository<RequestAggregate, RequestId>> Requests { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IUserDirectory> Directory { get; } = new();
        public Mock<IProjectDirectory> ProjectDirectory { get; } = new();
        public Mock<IRequestQuery> Query { get; } = new();
        public Mock<IActionLinkGenerator> ActionLinks { get; } = new();
        public Mock<INotificationSender> Notifications { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();

        public Sut()
        {
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Project));
            Requests.Setup(r => r.AddAsync(It.IsAny<RequestAggregate>(), It.IsAny<CancellationToken>()))
                .Returns<RequestAggregate, CancellationToken>((r, _) => ValueTask.FromResult<RequestAggregate?>(r));
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Domain.User.User?>(
                    Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), IdpId.Create("idp-req"))));
            Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
            ProjectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProjectId.New());
            Query.SetupSequence(q => q.FindPendingSelfOnboardingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid?)null)
                .ReturnsAsync(ReadBackId);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync() =>
            CreateRequestEndpoint.HandleAsync(
                new CreateRequestEndpoint.Request(ProjectShortNameInput, "SelfOnboarding"),
                new Caller(new IdpId("idp-req"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>()),
                Projects.Object, Requests.Object, Users.Object, Directory.Object, ProjectDirectory.Object,
                Query.Object, ActionLinks.Object, Notifications.Object, UnitOfWork.Object, Cache.Object,
                new RecordingAuditLogger<CreateRequestEndpoint.AuditCategory>(), NullLogger<CreateRequestEndpoint.AuditCategory>.Instance, CancellationToken.None);

        public void VerifyNothingWritten()
        {
            Requests.Verify(r => r.AddAsync(It.IsAny<RequestAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            ActionLinks.VerifyNoOtherCalls();
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
    public async Task Should_AddGenerateLinksSaveNotifyBothAndEvict_When_TheRequestIsCreated()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        var created = Assert.IsType<Created<CreateRequestEndpoint.Response>>(result);
        sut.Requests.Verify(r => r.AddAsync(
            It.Is<RequestAggregate>(a => a.Type == RequestType.SelfOnboarding && a.Slug == created.Value!.Slug && a.ContactEmail == RequesterEmail),
            It.IsAny<CancellationToken>()), Times.Once);
        sut.ActionLinks.Verify(a => a.Generate(It.Is<RequestAggregate>(r => r.Slug == created.Value!.Slug)), Times.Once);
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestCreatedToRequesterAsync(RequesterEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestCreatedToOwnerAsync(OwnerEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotifyOnlyTheRequester_When_TheProjectHasNoContactEmail()
    {
        var sut = new Sut { Project = BuildProject(email: null) };

        var result = await sut.InvokeAsync();

        Assert.IsType<Created<CreateRequestEndpoint.Response>>(result);
        sut.Notifications.Verify(n => n.SendRequestCreatedToRequesterAsync(RequesterEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotifyWithAnEmptyId_When_TheCreatedRequestCannotBeReadBack()
    {
        var sut = new Sut();
        sut.Query.SetupSequence(q => q.FindPendingSelfOnboardingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null)
            .ReturnsAsync((Guid?)null);

        await sut.InvokeAsync();

        sut.Notifications.Verify(n => n.SendRequestCreatedToRequesterAsync(RequesterEmail, Guid.Empty, ShortName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnRequired_When_TheProjectShortNameIsInvalid()
    {
        var sut = new Sut { ProjectShortNameInput = "" };

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
    public async Task Should_ReturnRequestNotFound_When_TheProjectIsMissing()
    {
        var sut = new Sut { Project = null };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Project not found.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnRequestNotFound_When_TheProjectIdCannotBeResolved()
    {
        var sut = new Sut();
        sut.ProjectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>())).ReturnsAsync((ProjectId?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Project not found.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnDuplicatePendingWithoutWriting_When_APendingRequestExists()
    {
        var sut = new Sut();
        sut.Query.SetupSequence(q => q.FindPendingSelfOnboardingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, RequestErrorCodes.DuplicatePendingRequest, "You already have a pending request for project 'PROJ-1'.");
        sut.VerifyNothingWritten();
    }
}
