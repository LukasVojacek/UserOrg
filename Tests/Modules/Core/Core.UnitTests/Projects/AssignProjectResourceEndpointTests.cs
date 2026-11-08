using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Project.AssignProjectResource;
using Core.Features.Project.UnassignProjectResource;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Projects;

/// <summary>
/// Handler-level tests for the resource↔project assign/unassign endpoints (F5 Phase D / ADR-0041). They
/// exercise the orchestration around the Project aggregate — provider-write authorization, the per-provider
/// resource resolution as an ENTITY by natural key (404, ADR-0041 L2.4), the cross-aggregate read-only guard
/// read off the loaded entity (422, L2.5), the project non-shared (409) guard, and the aggregate's own
/// already-assigned (409) / not-assigned (404) outcomes.
/// </summary>
public sealed class AssignProjectResourceEndpointTests
{
    private const string Provider = "acme";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";
    private const string Project = "PROJ-1";
    private const string ResName = "res-1";

    private static readonly ProjectShortName ShortName = ProjectShortName.Create(Project);

    private static Caller AuthorizedCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    // why: owns a DIFFERENT provider rather than none, so a 403 can only come from the ownership comparison - the whole gate for a non-Admin role since the identity kind left it (ADR-0105).
    private static Caller UnauthorizedCaller() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private const string PaddedProvider = "  AcMe  ";
    private const string MalformedProvider = " ";

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static ResourceAggregate NewResource(EOperationMode mode = EOperationMode.Editable, string name = ResName)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        return ResourceAggregate.Create(
            ResourceName.Create(name), provider, UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), operationMode: mode);
    }

    private static Mock<IRepository<ProjectAggregate, ProjectShortName>> ProjectRepo(ProjectAggregate? project)
    {
        var repo = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));
        return repo;
    }

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> ResourceRepo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static Mock<IResourceAssignmentQuery> AssignmentQuery()
    {
        var query = new Mock<IResourceAssignmentQuery>();
        // The cache-eviction surrogate lookup; behaviour does not depend on its result here.
        query.Setup(q => q.GetResourceSurrogateAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return query;
    }

    private static Mock<IProjectResourceAssignmentQuery> NonSharedQuery(bool hasNonShared)
    {
        var query = new Mock<IProjectResourceAssignmentQuery>();
        query.Setup(q => q.ProjectHasNonSharedResourceAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasNonShared);
        return query;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IOutputCacheInvalidator> Cache()
    {
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        return cache;
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Task<IResult> InvokeAssignAsync(
        Caller caller, ProjectAggregate? project, ResourceAggregate? resource,
        bool hasNonShared, Mock<IUnitOfWork> unitOfWork, string resourceName = ResName)
    {
        var request = new AssignProjectResourceEndpoint.Request(Project, Provider, resourceName);
        return AssignProjectResourceEndpoint.HandleAsync(
            request, caller, ProjectRepo(project).Object, ResourceRepo(resource).Object,
            AssignmentQuery().Object, NonSharedQuery(hasNonShared).Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, Cache().Object,
            new RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory>(), NullLogger<AssignProjectResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static Task<IResult> InvokeRemoveAsync(
        Caller caller, ProjectAggregate? project, ResourceAggregate? resource, Mock<IUnitOfWork> unitOfWork)
    {
        var request = new UnassignProjectResourceEndpoint.Request(Project, Provider, ResName);
        return UnassignProjectResourceEndpoint.HandleAsync(
            request, caller, ProjectRepo(project).Object, ResourceRepo(resource).Object,
            AssignmentQuery().Object, Options.Create(new WellKnownRoles()), unitOfWork.Object, Cache().Object,
            new RecordingAuditLogger<UnassignProjectResourceEndpoint.AuditCategory>(), NullLogger<UnassignProjectResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_AssignValid()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAssignAsync(AuthorizedCaller(), BuildProject(), NewResource(), hasNonShared: false, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_NotAuthorized()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAssignAsync(UnauthorizedCaller(), BuildProject(), NewResource(), hasNonShared: false, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectMissing()
    {
        var result = await InvokeAssignAsync(AuthorizedCaller(), project: null, NewResource(), hasNonShared: false, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ResourceMissingOrCrossProvider()
    {
        // A per-provider miss (unknown name, or a name owned by another provider) resolves to null → 404.
        var result = await InvokeAssignAsync(AuthorizedCaller(), BuildProject(), resource: null, hasNonShared: false, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.resource_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnUnprocessableAndNotSave_When_ResourceReadOnly()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAssignAsync(AuthorizedCaller(), BuildProject(), NewResource(EOperationMode.ReadOnly), hasNonShared: false, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnConflictAndNotSave_When_ProjectHasNonSharedResource()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAssignAsync(AuthorizedCaller(), BuildProject(), NewResource(), hasNonShared: true, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("project.non_shared_resource_conflict", problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnConflictAndNotSave_When_ResourceAlreadyAssigned()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess); // pre-existing active link
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAssignAsync(AuthorizedCaller(), project, resource, hasNonShared: false, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.ResourceAlreadyAssigned, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationWithRequiredCode_When_ResourceNameEmpty()
    {
        var result = await InvokeAssignAsync(AuthorizedCaller(), BuildProject(), NewResource(), hasNonShared: false, SavingUnitOfWork(), resourceName: "");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.Required, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_RemoveValid()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeRemoveAsync(AuthorizedCaller(), project, resource, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_RemoveResourceNotAssigned()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeRemoveAsync(AuthorizedCaller(), BuildProject(), NewResource(), unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.ResourceNotAssigned, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_RemoveNotAuthorized()
    {
        var project = BuildProject();
        var resource = NewResource();
        Assert.True(project.AssignResource(resource).IsSuccess);

        var result = await InvokeRemoveAsync(UnauthorizedCaller(), project, resource, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnUnprocessableAndNotSave_When_RemoveResourceReadOnly()
    {
        var project = BuildProject();
        var resource = NewResource(EOperationMode.ReadOnly);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeRemoveAsync(AuthorizedCaller(), project, resource, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Authorize-before-validate ordering (#261b) ───────────────────────────
    // Both project-resource writes authorize the raw provider name before parsing it, so a malformed name never
    // leaks — via a 400 — that it reached validation: a non-admin sees 403, an authorized caller still sees the
    // 400. Owner equality survives the raw-string path (ADR-0012); admin still validates once authorized.

    private static Task<IResult> InvokeAssignRawAsync(Caller caller, string providerName) =>
        AssignProjectResourceEndpoint.HandleAsync(
            new AssignProjectResourceEndpoint.Request(Project, providerName, ResName), caller,
            ProjectRepo(project: null).Object, ResourceRepo(resource: null).Object,
            AssignmentQuery().Object, NonSharedQuery(hasNonShared: false).Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, Cache().Object,
            new RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory>(), NullLogger<AssignProjectResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeRemoveRawAsync(Caller caller, string providerName) =>
        UnassignProjectResourceEndpoint.HandleAsync(
            new UnassignProjectResourceEndpoint.Request(Project, providerName, ResName), caller,
            ProjectRepo(project: null).Object, ResourceRepo(resource: null).Object,
            AssignmentQuery().Object, Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, Cache().Object,
            new RecordingAuditLogger<UnassignProjectResourceEndpoint.AuditCategory>(), NullLogger<UnassignProjectResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> Dispatch(string endpoint, Caller caller, string providerName) => endpoint switch
    {
        "AssignProjectResource" => InvokeAssignRawAsync(caller, providerName),
        "UnassignProjectResource" => InvokeRemoveRawAsync(caller, providerName),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
    };

    public static TheoryData<string> ReorderedEndpoints() => new()
    {
        "AssignProjectResource", "UnassignProjectResource",
    };

    [Theory]
    [MemberData(nameof(ReorderedEndpoints))]
    public async Task Should_Return403NotBadRequest_When_NonAdminSendsMalformedProviderName(string endpoint)
    {
        var result = await Dispatch(endpoint, UnauthorizedCaller(), MalformedProvider);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Theory]
    [MemberData(nameof(ReorderedEndpoints))]
    public async Task Should_ReturnBadRequest_When_AdminSendsMalformedProviderName(string endpoint)
    {
        var result = await Dispatch(endpoint, AdminCaller(), MalformedProvider);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidName, problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [MemberData(nameof(ReorderedEndpoints))]
    public async Task Should_Authorize_When_OwnerSendsPaddedProviderName(string endpoint)
    {
        var result = await Dispatch(endpoint, AuthorizedCaller(), PaddedProvider);

        // Padded/differently-cased owner name authorizes and parses, so the flow reaches the (absent) project
        // and returns 404 — never 403 (auth intact) or 400 (parsed after auth).
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
    }
}
