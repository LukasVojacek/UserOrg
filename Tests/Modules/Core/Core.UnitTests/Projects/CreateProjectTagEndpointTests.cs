using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Project.CreateProjectTag;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class CreateProjectTagEndpointTests
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

    private static CreateProjectTagEndpoint.Request Req(string tagName = "hpc") =>
        new(ShortName, new CreateProjectTagEndpoint.Body(tagName));

    private static UserDetails Actor() =>
        new(new IdpId("u"), Email.Create("u@example.com"), UserName.Create("author"));

    /// <summary>
    /// Default tag repository: no existing tag (<c>FindAsync</c> → null) and an ensure-add that returns the
    /// aggregate it was handed (the genuine-insert path). Individual tests override to model revive.
    /// </summary>
    private static Mock<IRepository<ProjectTag, TagName>> TagRepository()
    {
        var tags = new Mock<IRepository<ProjectTag, TagName>>();
        tags.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectTag?>(null));
        tags.Setup(r => r.AddAsync(It.IsAny<ProjectTag>(), It.IsAny<CancellationToken>()))
            .Returns((ProjectTag t, CancellationToken _) => ValueTask.FromResult<ProjectTag?>(t));
        return tags;
    }

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        Domain.Project.Project? project,
        CreateProjectTagEndpoint.Request request,
        UserDetails? actor = null,
        Mock<IRepository<ProjectTag, TagName>>? tags = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IOutputCacheInvalidator>? cache = null)
    {
        var repository = new Mock<IRepository<Domain.Project.Project, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        tags ??= TagRepository();

        var users = new Mock<IUserInfoQuery>();
        users.Setup(u => u.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor);

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        cache ??= new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await CreateProjectTagEndpoint.HandleAsync(
            request, caller, repository.Object, tags.Object, users.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<CreateProjectTagEndpoint.AuditCategory>(), NullLogger<CreateProjectTagEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnCreated_When_CreatesAndAssigns()
    {
        var project = BuildProject();
        var tags = TagRepository();
        var unitOfWork = new Mock<IUnitOfWork>();
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(CallerInScope(), project, Req(), Actor(), tags, unitOfWork, cache);

        var created = Assert.IsType<Created<CreateProjectTagEndpoint.Response>>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("hpc", created.Value!.Name);
        Assert.Contains(TagName.Create("hpc"), project.Tags);
        tags.Verify(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()), Times.Once);
        tags.Verify(r => r.AddAsync(It.IsAny<ProjectTag>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnCreated_And_AssignRevivedTag_When_NameMatchesSoftDeletedTag()
    {
        var project = BuildProject();

        // Model the adapter's ensure semantics: FindAsync (which filters !IsDeleted) misses, and AddAsync
        // restores + returns the soft-deleted row rather than inserting a duplicate.
        var revived = ProjectTag.Create(TagName.Create("hpc"), UserName.Create("author"));
        revived.Delete();
        var tags = new Mock<IRepository<ProjectTag, TagName>>();
        tags.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectTag?>(null));
        tags.Setup(r => r.AddAsync(It.IsAny<ProjectTag>(), It.IsAny<CancellationToken>()))
            .Returns((ProjectTag _, CancellationToken __) =>
            {
                revived.Restore();
                return ValueTask.FromResult<ProjectTag?>(revived);
            });

        var result = await InvokeAsync(CallerInScope(), project, Req(), Actor(), tags);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        // The tag assigned to the project is the revived catalogue row returned by AddAsync, now active.
        Assert.False(revived.IsDeleted);
        Assert.Contains(revived.Name, project.Tags);
        tags.Verify(r => r.AddAsync(It.IsAny<ProjectTag>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnCreated_When_TagAlreadyAssigned()
    {
        var project = BuildProject();
        project.AssignExistingTag(ProjectTag.Create(TagName.Create("hpc"), UserName.Create("author")));
        var tags = TagRepository();

        var result = await InvokeAsync(CallerInScope(), project, Req(), Actor(), tags);

        // Idempotent, but still addressable → uniform 201 + Location (the tag is not re-created).
        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        tags.Verify(r => r.AddAsync(It.IsAny<ProjectTag>(), It.IsAny<CancellationToken>()), Times.Never);
        tags.Verify(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_NameEmpty()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(), Req("   "), Actor());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.TagNameInvalid, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "ProjectManager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, BuildProject(), Req(), Actor());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAsync(CallerInScope(), project: null, Req(), Actor());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ActorMissing()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(), Req(), actor: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(EOperationMode.ReadOnly), Req(), Actor());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }
}
