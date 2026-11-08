using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.HandleActionLink;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class HandleActionLinkEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string Token = "the-token";

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static RequestAggregate BuildRequest()
    {
        var request = RequestAggregate.Create(Email.Create("req@example.com"), ProjectId.New(), UserId.New(), RequestType.SelfOnboarding);
        request.ClearDomainEvents();
        return request;
    }

    private static ProjectAggregate BuildProject()
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        project.ClearDomainEvents();
        return project;
    }

    private static async Task<IResult> InvokeAsync(ActionLinkTarget? target)
    {
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.FindActionLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<RequestAggregate?>(BuildRequest()));

        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(Domain.User.User.CreateUser(
                Email.Create("req@example.com"), UserName.Create("requester"), IdpId.Create("idp-req"))));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdpId.Create("idp-req"));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync(ShortName);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>())).ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        var notifications = new Mock<INotificationSender>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await HandleActionLinkEndpoint.HandleAsync(
            new HandleActionLinkEndpoint.Request(Token),
            query.Object, requests.Object, projects.Object, users.Object, directory.Object,
            projectDirectory.Object, roleList.Object,
            Options.Create(new WellKnownRoles()), notifications.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_ReturnOk_When_AcceptLink()
    {
        var result = await InvokeAsync(new ActionLinkTarget(Guid.NewGuid(), ActionLinkAction.Accept, DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnOk_When_RejectLink()
    {
        var result = await InvokeAsync(new ActionLinkTarget(Guid.NewGuid(), ActionLinkAction.Reject, DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidActionLinkCode_When_TokenUnknown()
    {
        var result = await InvokeAsync(target: null);

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.InvalidActionLink, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidActionLinkCode_When_LinkExpired()
    {
        var result = await InvokeAsync(new ActionLinkTarget(Guid.NewGuid(), ActionLinkAction.Accept, DateTimeOffset.UtcNow.AddHours(-1)));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.InvalidActionLink, CodeOf(result));
    }
}
