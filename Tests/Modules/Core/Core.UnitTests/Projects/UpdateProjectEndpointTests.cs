using Core.Domain.Shared;
using Core.Features.Project.UpdateProject;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class UpdateProjectEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Domain.Project.Project BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = Domain.Project.Project.Create(
            ShortName,
            ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            UserId.New(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            ServiceName.Default,
            mode);
        project.ClearDomainEvents();
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

    private static UpdateProjectEndpoint.Request ValidRequest() => new(
        Name: "Renamed",
        Description: "updated",
        ContactEmail: "owner@example.com",
        StartDate: DateTimeOffset.UtcNow,
        EndDate: DateTimeOffset.UtcNow.AddDays(60),
        DomainTypeName: "Engineering");

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        Domain.Project.Project? project,
        ProjectDomainType? domainType,
        UpdateProjectEndpoint.Request request,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IOutputCacheInvalidator>? cache = null)
    {
        var repository = new Mock<IRepository<Domain.Project.Project, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));

        var domainTypes = new Mock<IProjectDomainTypeLookup>();
        domainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(domainType);

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        cache ??= new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await UpdateProjectEndpoint.HandleAsync(
            ShortName, request, caller, repository.Object, domainTypes.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<UpdateProjectEndpoint.AuditCategory>(), NullLogger<UpdateProjectEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static void AssertValidationProblem(IResult result, string expectedCode)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(expectedCode, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_ValidUpdate()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var cache = new Mock<IOutputCacheInvalidator>();

        var result = await InvokeAsync(
            CallerInScope(),
            BuildProject(),
            ProjectDomainType.FromName(DomainTypeName.Create("Engineering")),
            ValidRequest(),
            unitOfWork,
            cache);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var caller = new Caller(new IdpId("u"), "ProjectManager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller, BuildProject(), ProjectDomainType.FromName(DomainTypeName.Create("HPC")), ValidRequest());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAsync(CallerInScope(), project: null, ProjectDomainType.FromName(DomainTypeName.Create("HPC")), ValidRequest());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_MissingOrOutOfScope()
    {
        var outOfScope = new Caller(new IdpId("u"), "ProjectManager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var hidden = Assert.IsType<ProblemHttpResult>(
            await InvokeAsync(outOfScope, BuildProject(), ProjectDomainType.FromName(DomainTypeName.Create("HPC")), ValidRequest()).ConfigureAwait(false));
        var missing = Assert.IsType<ProblemHttpResult>(
            await InvokeAsync(CallerInScope(), project: null, ProjectDomainType.FromName(DomainTypeName.Create("HPC")), ValidRequest()).ConfigureAwait(false));

        // Existence must not leak: scope-hidden and genuinely-missing are indistinguishable (ADR-0018 rider).
        Assert.Equal(StatusCodes.Status404NotFound, hidden.StatusCode);
        Assert.Equal("project.not_found", hidden.ProblemDetails.Extensions["code"]);
        Assert.Equal(hidden.ProblemDetails.Extensions["code"], missing.ProblemDetails.Extensions["code"]);
        Assert.Equal(hidden.ProblemDetails.Detail, missing.ProblemDetails.Detail);
        Assert.Equal(hidden.ProblemDetails.Title, missing.ProblemDetails.Title);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ProjectReadOnly()
    {
        var result = await InvokeAsync(
            CallerInScope(),
            BuildProject(EOperationMode.ReadOnly),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            ValidRequest());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_DomainTypeMissing()
    {
        var result = await InvokeAsync(CallerInScope(), BuildProject(), domainType: null, ValidRequest());

        AssertValidationProblem(result, ProjectErrorCodes.DomainTypeNotFound);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_DomainTypeNameEmpty()
    {
        var result = await InvokeAsync(
            CallerInScope(),
            BuildProject(),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            ValidRequest() with { DomainTypeName = "   " });

        AssertValidationProblem(result, ProjectErrorCodes.DomainTypeNameInvalid);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_NameEmpty()
    {
        var result = await InvokeAsync(
            CallerInScope(),
            BuildProject(),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            ValidRequest() with { Name = "   " });

        AssertValidationProblem(result, ProjectErrorCodes.NameInvalid);
    }
}
