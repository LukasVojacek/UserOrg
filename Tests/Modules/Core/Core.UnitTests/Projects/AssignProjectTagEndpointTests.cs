using Core.Domain.Shared;
using Core.Features.Project.AssignProjectTag;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class AssignProjectTagEndpointTests
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

    private static Caller CallerInScope() => new(
        new IdpId("u"),
        "ProjectManager",
        null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["ProjectManager"] = new HashSet<ProjectShortName> { ShortName },
        });

    private static AssignProjectTagEndpoint.Request Req(string tagName = "hpc") => new(ShortName, tagName);

    /// <summary>A tag repository whose FindAsync returns an existing catalogue tag for any name (assign-existing happy path).</summary>
    private static Mock<IRepository<ProjectTag, TagName>> ExistingTagRepository()
    {
        var tags = new Mock<IRepository<ProjectTag, TagName>>();
        tags.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns((TagName n, CancellationToken _) => ValueTask.FromResult<ProjectTag?>(ProjectTag.Create(n, UserName.Create("author"))));
        return tags;
    }

    /// <summary>
    /// A tag repository whose FindAsync returns null for any name. Covers both a name that never existed
    /// and a soft-deleted name: <c>FindAsync</c> filters <c>!IsDeleted</c>, so a soft-deleted tag reads as
    /// missing and assign never revives it (404 — use POST to create).
    /// </summary>
    private static Mock<IRepository<ProjectTag, TagName>> MissingTagRepository()
    {
        var tags = new Mock<IRepository<ProjectTag, TagName>>();
        tags.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectTag?>(null));
        return tags;
    }

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        Domain.Project.Project? project,
        AssignProjectTagEndpoint.Request request,
        Mock<IRepository<ProjectTag, TagName>> tags,
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

        return await AssignProjectTagEndpoint.HandleAsync(
            request, caller, repository.Object, tags.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<AssignProjectTagEndpoint.AuditCategory>(), NullLogger<AssignProjectTagEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_AssigningExistingTag()
    {
        var project = BuildProject();
        var tags = ExistingTagRepository();
        var unitOfWork = new Mock<IUnitOfWork>();
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(CallerInScope(), project, Req(), tags, unitOfWork, cache);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Contains(TagName.Create("hpc"), project.Tags);
        tags.Verify(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFound_With_TagNotFoundCode_When_TagDoesNotExist()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(), Req(), MissingTagRepository());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        // A missing tag is a distinct resource from the project — different code.
        Assert.Equal("project.tag_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_With_TagNotFoundCode_When_TagIsSoftDeleted()
    {
        // FindAsync filters !IsDeleted, so a soft-deleted tag reads as null — assign 404s (never revives).
        var result = await InvokeAsync(CallerInScope(), BuildProject(), Req(), MissingTagRepository());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.tag_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_With_ProjectNotFoundCode_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "ProjectManager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, BuildProject(), Req(), ExistingTagRepository());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAsync(CallerInScope(), project: null, Req(), ExistingTagRepository());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_TagNameEmpty()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(), Req("   "), ExistingTagRepository());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.TagNameInvalid, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly), Req(), ExistingTagRepository());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }
}
