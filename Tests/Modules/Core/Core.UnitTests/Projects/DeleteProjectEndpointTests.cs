using Core.Domain.Shared;
using Core.Features.Project.DeleteProject;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

public sealed class DeleteProjectEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static async Task<IResult> InvokeAsync(
        Caller caller, ProjectAggregate? project, Mock<IOutputCacheInvalidator>? cache = null)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        cache ??= new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await DeleteProjectEndpoint.HandleAsync(
            ShortName, caller, repository.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_And_EvictAffectedUsers_When_Deleting()
    {
        var project = BuildProject();
        project.AssignUserWithRole(
            Domain.User.User.CreateUser(Email.Create("a@example.com"), UserName.Create("user-a"), IdpId.Create("user-a")),
            UserId.New(), ProjectRoleId.Create(3));
        project.AssignUserWithRole(
            Domain.User.User.CreateUser(Email.Create("b@example.com"), UserName.Create("user-b"), IdpId.Create("user-b")),
            UserId.New(), ProjectRoleId.Create(3));
        project.ClearDomainEvents();
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(CallerInScope(), project, cache);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.UserInfo("user-a"), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.UserInfo("user-b"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, BuildProject());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAsync(CallerInScope(), project: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }
}
