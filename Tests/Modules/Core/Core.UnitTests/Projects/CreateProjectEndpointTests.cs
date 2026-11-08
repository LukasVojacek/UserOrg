using Core.Domain;
using Core.Features.Project.CreateProject;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

public sealed class CreateProjectEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller AnyCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static CreateProjectEndpoint.Request ValidRequest() => new(
        ShortName: "PROJ-1", Name: "Project One", Description: null, ContactEmail: null,
        StartDate: DateTimeOffset.UtcNow, EndDate: DateTimeOffset.UtcNow.AddDays(30), DomainTypeName: "HPC");

    private static async Task<IResult> InvokeAsync(
        Caller caller, CreateProjectEndpoint.Request request, ProjectAggregate? existing, ProjectDomainType? domainType) =>
        (await InvokeWithUowAsync(caller, request, existing, domainType).ConfigureAwait(false)).Result;

    private static async Task<(IResult Result, Mock<IUnitOfWork> UnitOfWork)> InvokeWithUowAsync(
        Caller caller, CreateProjectEndpoint.Request request, ProjectAggregate? existing, ProjectDomainType? domainType)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(existing));
        repository.Setup(r => r.AddAsync(It.IsAny<ProjectAggregate>(), It.IsAny<CancellationToken>()))
            .Returns((ProjectAggregate p, CancellationToken _) => ValueTask.FromResult<ProjectAggregate?>(p));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(Domain.User.User.CreateUser(
                Email.Create("u@example.com"), UserName.Create("creator"), IdpId.Create("creator-idp"))));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());

        var domainTypes = new Mock<IProjectDomainTypeLookup>();
        domainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(domainType);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        var result = await CreateProjectEndpoint.HandleAsync(
            request, caller, repository.Object, users.Object, directory.Object, domainTypes.Object,
            roleList.Object, Options.Create(new WellKnownRoles()),
            unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<CreateProjectEndpoint.AuditCategory>(), NullLogger<CreateProjectEndpoint.AuditCategory>.Instance, new DefaultHttpContext(), CancellationToken.None);

        return (result, unitOfWork);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static void AssertValidationProblem(IResult result, string expectedCode)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(expectedCode, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnCreated_When_Valid()
    {
        var domainType = ProjectDomainType.FromName(DomainTypeName.Create("HPC"));

        var result = await InvokeAsync(AnyCaller(), ValidRequest(), existing: null, domainType);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Should_PersistProjectAndOwnerInSingleCommit_When_Valid()
    {
        // Regression: the project and its owner grant must commit atomically. A second SaveChangesAsync
        // would leave an owner-less project persisted if the grant or the process failed in between.
        var domainType = ProjectDomainType.FromName(DomainTypeName.Create("HPC"));

        var (result, unitOfWork) = await InvokeWithUowAsync(AnyCaller(), ValidRequest(), existing: null, domainType);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Should_ReturnConflict_With_ShortNameAlreadyExistsCode_When_ShortNameAlreadyExists()
    {
        var existing = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Existing"), ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            UserId.New(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), ServiceName.Default);

        var result = await InvokeAsync(AnyCaller(), ValidRequest(), existing, ProjectDomainType.FromName(DomainTypeName.Create("HPC")));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("project.short_name_already_exists", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_DomainTypeMissing()
    {
        var result = await InvokeAsync(AnyCaller(), ValidRequest(), existing: null, domainType: null);

        AssertValidationProblem(result, ProjectErrorCodes.DomainTypeNotFound);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_NameEmpty()
    {
        var result = await InvokeAsync(AnyCaller(), ValidRequest() with { Name = "  " }, existing: null,
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")));

        AssertValidationProblem(result, ProjectErrorCodes.NameInvalid);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_EndDateBeforeStartDate()
    {
        var request = ValidRequest() with { StartDate = DateTimeOffset.UtcNow, EndDate = DateTimeOffset.UtcNow.AddDays(-1) };

        var result = await InvokeAsync(AnyCaller(), request, existing: null,
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")));

        AssertValidationProblem(result, ProjectErrorCodes.EndDateBeforeStartDate);
    }
}
