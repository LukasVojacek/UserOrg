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
/// Characterization tests pinning the guard order, status and code of
/// <see cref="AssignProjectResourceEndpoint.HandleAsync"/> and <see cref="UnassignProjectResourceEndpoint.HandleAsync"/>
/// that the existing suite leaves open, before the guards are extracted into private methods (T-197-03).
/// </summary>
public sealed class ProjectResourceAssignmentEndpointsCharacterizationTests
{
    private const string Assign = "AssignProjectResource";
    private const string Unassign = "UnassignProjectResource";
    private const string Provider = "acme";
    private const string Project = "PROJ-1";
    private const string ResName = "res-1";

    private static readonly Guid Surrogate = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static TheoryData<string> Endpoints() => new() { Assign, Unassign };

    private sealed class Fixture
    {
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();
        public Mock<IRepository<ResourceAggregate, ProjectResourceKey>> Resources { get; } = new();
        public Mock<IResourceAssignmentQuery> AssignmentQuery { get; } = new();
        public Mock<IProjectResourceAssignmentQuery> NonShared { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public List<string> EvictedTags { get; } = [];
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();

        public Fixture(ProjectAggregate? project, ResourceAggregate? resource)
        {
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult(project));
            Resources.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult(resource));
            AssignmentQuery.Setup(q => q.GetResourceSurrogateAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Surrogate);
            NonShared.Setup(q => q.ProjectHasNonSharedResourceAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string tag, CancellationToken _) => EvictedTags.Add(tag))
                .Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync(
            string endpoint, Caller caller, string project = Project, string provider = Provider, string resource = ResName) =>
            endpoint switch
            {
                Assign => AssignProjectResourceEndpoint.HandleAsync(
                    new AssignProjectResourceEndpoint.Request(project, provider, resource), caller,
                    Projects.Object, Resources.Object, AssignmentQuery.Object, NonShared.Object,
                    Options.Create(new WellKnownRoles()), UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory>(), NullLogger<AssignProjectResourceEndpoint.AuditCategory>.Instance, CancellationToken.None),
                Unassign => UnassignProjectResourceEndpoint.HandleAsync(
                    new UnassignProjectResourceEndpoint.Request(project, provider, resource), caller,
                    Projects.Object, Resources.Object, AssignmentQuery.Object,
                    Options.Create(new WellKnownRoles()), UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<UnassignProjectResourceEndpoint.AuditCategory>(), NullLogger<UnassignProjectResourceEndpoint.AuditCategory>.Instance, CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
            };

        public void VerifyNothingWritten()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(EvictedTags);
        }
    }

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ProjectShortName.Create(Project), ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static ResourceAggregate NewResource(EOperationMode mode = EOperationMode.Editable)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        return ResourceAggregate.Create(
            ResourceName.Create(ResName), provider, UserId.From(Guid.NewGuid()), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), operationMode: mode);
    }

    private static (ProjectAggregate Project, ResourceAggregate Resource) ReadyFor(string endpoint)
    {
        var project = BuildProject();
        var resource = NewResource();
        if (endpoint == Unassign)
        {
            project.AssignResource(resource);
            project.ClearDomainEvents();
        }

        return (project, resource);
    }

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnShortNameInvalid_Before_ProviderNameCheck(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), NewResource());

        var result = await fixture.InvokeAsync(endpoint, AdminCaller(), project: " ", provider: " ", resource: " ");

        AssertProblem(result, StatusCodes.Status400BadRequest, "project.short_name_invalid");
        fixture.Projects.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnProviderNameInvalid_Before_ResourceNameCheck(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), NewResource());

        var result = await fixture.InvokeAsync(endpoint, AdminCaller(), provider: " ", resource: " ");

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidName);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnRequired_When_ResourceNameEmpty(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), NewResource());

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller(), resource: " ");

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.Required);
        fixture.Projects.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnProjectNotFound_Before_ResourceLookup(string endpoint)
    {
        var fixture = new Fixture(project: null, NewResource());

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller());

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        fixture.Resources.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnResourceNotFound_When_ResourceMissing(string endpoint)
    {
        var fixture = new Fixture(BuildProject(), resource: null);

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller());

        AssertProblem(result, StatusCodes.Status404NotFound, "provider.resource_not_found");
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnProviderReadOnly_Before_AnyFurtherCheck_When_ResourceReadOnly(string endpoint)
    {
        var fixture = new Fixture(BuildProject(EOperationMode.ReadOnly), NewResource(EOperationMode.ReadOnly));

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller());

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, ProviderErrorCodes.ProviderReadOnly);
        fixture.NonShared.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnNonSharedConflict_Before_ProjectReadOnlyCheck()
    {
        var fixture = new Fixture(BuildProject(EOperationMode.ReadOnly), NewResource());
        fixture.NonShared.Setup(q => q.ProjectHasNonSharedResourceAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.InvokeAsync(Assign, OwnerCaller());

        AssertProblem(result, StatusCodes.Status409Conflict, "project.non_shared_resource_conflict");
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_ReturnProjectReadOnly_When_ProjectIsReadOnly(string endpoint)
    {
        var fixture = new Fixture(BuildProject(EOperationMode.ReadOnly), NewResource());

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller());

        AssertProblem(result, StatusCodes.Status409Conflict, ProjectErrorCodes.ReadOnly);
        fixture.VerifyNothingWritten();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_SaveAndEvictProjectThenResourceTag_When_Valid(string endpoint)
    {
        var (project, resource) = ReadyFor(endpoint);
        var fixture = new Fixture(project, resource);

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller());

        Assert.IsType<NoContent>(result);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([CacheTags.Project(Project), CacheTags.ProjectResource(Surrogate)], fixture.EvictedTags);
        fixture.AssignmentQuery.Verify(
            q => q.GetResourceSurrogateAsync(new ProjectResourceKey(ProviderName.From(Provider), ResourceName.Create(ResName)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_EvictOnlyTheProjectTag_When_ResourceSurrogateIsUnknown(string endpoint)
    {
        var (project, resource) = ReadyFor(endpoint);
        var fixture = new Fixture(project, resource);
        fixture.AssignmentQuery.Setup(q => q.GetResourceSurrogateAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await fixture.InvokeAsync(endpoint, OwnerCaller());

        Assert.IsType<NoContent>(result);
        Assert.Equal([CacheTags.Project(Project)], fixture.EvictedTags);
    }
}
