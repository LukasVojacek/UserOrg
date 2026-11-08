using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class SelfOnboardingAcceptTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly IdpId RequesterIdpId = IdpId.Create("idp-req");
    private static readonly Email RequesterEmail = Email.Create("req@example.com");

    private sealed class Sut
    {
        public RequestAggregate Request { get; } = BuildRequest();

        public ProjectAggregate Project { get; set; } = BuildProject(EOperationMode.Editable);

        public Domain.User.User Requester { get; } =
            Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), RequesterIdpId);

        public Mock<IUserDirectory> Directory { get; } = new();

        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();

        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();

        public Mock<IRoleListService> Roles { get; } = new();

        public Sut()
        {
            Directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(RequesterIdpId);
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Domain.User.User?>(Requester));
            Projects.Setup(p => p.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<ProjectAggregate?>(Project));
            Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(
                [
                    new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
                    new RoleInfo(3, "MEMBER", "Member", "Project", [], true, 10),
                ]));
        }

        public Task<SelfOnboardingAcceptOutcome> ExecuteAsync() =>
            SelfOnboardingAccept.ExecuteAsync(
                Request, ShortName, UserId.New(),
                Projects.Object, Users.Object, Directory.Object, Roles.Object, new WellKnownRoles(),
                CancellationToken.None);
    }

    private static RequestAggregate BuildRequest()
    {
        var request = RequestAggregate.Create(RequesterEmail, ProjectId.New(), UserId.New(), RequestType.SelfOnboarding);
        request.ClearDomainEvents();
        return request;
    }

    private static ProjectAggregate BuildProject(EOperationMode mode)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static void AssertProblem(IResult? result, int status, string code, string? detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        if (detail is not null)
            Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_AcceptAndGrantTheMemberRoleMatchedIgnoringCase_When_EverythingResolves()
    {
        var sut = new Sut();

        var outcome = await sut.ExecuteAsync();

        Assert.Null(outcome.Failure);
        Assert.Equal(RequesterEmail, outcome.RequesterEmail);
        Assert.Equal(RequesterIdpId, outcome.RequesterIdpId);
        Assert.Equal(RequestState.Accepted, sut.Request.State);
        Assert.Equal(ProjectRoleId.Create(3), sut.Project.ActiveRoleOf(sut.Requester));
    }

    [Fact]
    public async Task Should_ReturnRequestNotFound_When_TheRequesterHasNoIdentity()
    {
        var sut = new Sut();
        sut.Directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdpId?)null);

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        Assert.Null(outcome.RequesterEmail);
        Assert.Equal(default, outcome.RequesterIdpId);
        Assert.Equal(RequestState.Pending, sut.Request.State);
    }

    [Fact]
    public async Task Should_ReturnRequestNotFound_When_TheRequesterUserIsMissing()
    {
        var sut = new Sut();
        sut.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        Assert.Equal(RequestState.Pending, sut.Request.State);
    }

    [Fact]
    public async Task Should_ReturnRequestNotFound_When_TheProjectIsMissing()
    {
        var sut = new Sut();
        sut.Projects.Setup(p => p.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(null));

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        Assert.Equal(RequestState.Pending, sut.Request.State);
    }

    [Fact]
    public async Task Should_ReturnRoleServiceUnavailable_When_TheRolesCannotBeListed()
    {
        var sut = new Sut();
        sut.Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Failed to retrieve roles.");
        Assert.Equal(RequestState.Pending, sut.Request.State);
    }

    [Fact]
    public async Task Should_ReturnRoleServiceUnavailable_When_TheMemberRoleIsNotConfigured()
    {
        var sut = new Sut();
        sut.Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20)]));

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Project member role is not configured.");
        Assert.Equal(RequestState.Pending, sut.Request.State);
    }

    [Fact]
    public async Task Should_ReturnInvalidStateTransition_When_TheRequestIsAlreadySettled()
    {
        var sut = new Sut();
        Assert.True(sut.Request.Reject().IsSuccess);

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status409Conflict, RequestErrorCodes.InvalidStateTransition, null);
        Assert.Null(sut.Project.ActiveRoleOf(sut.Requester));
    }

    [Fact]
    public async Task Should_ReturnTheAssignFailure_When_TheProjectIsReadOnly()
    {
        var sut = new Sut { Project = BuildProject(EOperationMode.ReadOnly) };

        var outcome = await sut.ExecuteAsync();

        AssertProblem(outcome.Failure, StatusCodes.Status409Conflict, "project.read_only", null);
        Assert.Null(outcome.RequesterEmail);
        Assert.Equal(RequestState.Accepted, sut.Request.State);
    }
}
