using Core.Domain.Shared;
using Core.Features.Project.UnassignProjectTag;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class UnassignProjectTagEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Domain.Project.Project BuildProject(EOperationMode mode = EOperationMode.Editable) =>
        Domain.Project.Project.Create(
            ShortName,
            ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            UserId.New(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            ServiceName.Default,
            mode);

    private static Domain.Project.Project BuildProjectWithTag()
    {
        var project = BuildProject();
        project.AssignExistingTag(ProjectTag.Create(TagName.Create("hpc"), UserName.Create("author")));
        return project;
    }

    private static Caller CallerInScope() => new(
        new IdpId("u"),
        "ProjectManager",
        null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["ProjectManager"] = new HashSet<ProjectShortName> { ShortName },
        });

    private static UnassignProjectTagEndpoint.Request Req(string tagName = "hpc") => new(ShortName, tagName);

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        Domain.Project.Project? project,
        UnassignProjectTagEndpoint.Request request,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IOutputCacheInvalidator>? cache = null)
    {
        var repository = new Mock<IRepository<Domain.Project.Project, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        cache ??= new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await UnassignProjectTagEndpoint.HandleAsync(
            request, caller, repository.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<UnassignProjectTagEndpoint.AuditCategory>(), NullLogger<UnassignProjectTagEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_ValidUnassign()
    {
        var project = BuildProjectWithTag();
        var unitOfWork = new Mock<IUnitOfWork>();
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(CallerInScope(), project, Req(), unitOfWork, cache);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Empty(project.Tags);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_TagNotAssigned()
    {
        // Idempotent: removing a tag the project does not have is a no-op success.
        var result = await InvokeAsync(CallerInScope(), BuildProject(), Req("absent"));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "ProjectManager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, BuildProjectWithTag(), Req());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAsync(CallerInScope(), project: null, Req());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_TagNameEmpty()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProjectWithTag(), Req("   "));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.TagNameInvalid, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        // Read-only is rejected before the tag-presence check.
        var result = await InvokeAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly), Req());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }
}
