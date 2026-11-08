using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.UnassignProjectUser;
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

namespace Core.UnitTests.Projects;

public sealed class UnassignProjectUserEndpointEarlyExitTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static ProjectAggregate BuildProject()
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        project.ClearDomainEvents();
        return project;
    }

    private static async Task<(ProblemHttpResult Problem, RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory> Audit)> InvokeAsync(
        bool callerProvisioned = true,
        bool targetExists = true,
        bool rolesSucceed = true,
        bool ownerRoleConfigured = true,
        bool projectExists = true)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(projectExists ? BuildProject() : null));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(targetExists
                ? Domain.User.User.CreateUser(Email.Create("t@example.com"), UserName.Create("target"), IdpId.Create(TargetIdp))
                : null));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerProvisioned ? UserId.New() : (UserId?)null);

        IList<RoleInfo> roles = ownerRoleConfigured
            ? [new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20), new RoleInfo(3, "Member", "Member", "Project", [], true, 10)]
            : [new RoleInfo(3, "Member", "Member", "Project", [], true, 10)];
        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rolesSucceed
                ? Result<IList<RoleInfo>, Error>.Success(roles)
                : Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var audit = new RecordingAuditLogger<UnassignProjectUserEndpoint.AuditCategory>();

        var result = await UnassignProjectUserEndpoint.HandleAsync(
            new UnassignProjectUserEndpoint.Request(ShortName, TargetIdp),
            CallerInScope(), repository.Object, users.Object, directory.Object, roleList.Object,
            Options.Create(new WellKnownRoles()), Mock.Of<IUnitOfWork>(), Mock.Of<IOutputCacheInvalidator>(),
            audit, new DefaultHttpContext(), CancellationToken.None);

        return (Assert.IsType<ProblemHttpResult>(result), audit);
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisionedWithoutAuditing_When_CallerNotInDirectory()
    {
        var (problem, audit) = await InvokeAsync(callerProvisioned: false);

        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("caller.not_provisioned", problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ReturnUserNotFoundWithoutAuditing_When_TargetMissing()
    {
        var (problem, audit) = await InvokeAsync(targetExists: false);

        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.user_not_found", problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ReturnBadGatewayWithoutAuditing_When_RoleServiceFails()
    {
        var (problem, audit) = await InvokeAsync(rolesSucceed: false);

        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Failed to retrieve roles.", problem.ProblemDetails.Detail);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ReturnBadGatewayWithoutAuditing_When_OwnerRoleNotConfigured()
    {
        var (problem, audit) = await InvokeAsync(ownerRoleConfigured: false);

        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Project owner role is not configured.", problem.ProblemDetails.Detail);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ReturnProjectNotFoundWithoutAuditing_When_ProjectMissing()
    {
        var (problem, audit) = await InvokeAsync(projectExists: false);

        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(audit.Events);
    }
}
