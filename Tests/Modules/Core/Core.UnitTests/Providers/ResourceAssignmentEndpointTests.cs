using System.Text.Json;
using System.Text.Json.Serialization;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.ResourceAssignment;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

using ProviderAggregate = Core.Domain.Provider.Provider;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Providers;

/// <summary>
/// Handler-level tests for the resource-assignment write endpoints (ADR-0016 / ADR-0041, S4). They exercise
/// the orchestration around the aggregate — authorization, validation, and the cross-aggregate guards. The
/// resource is now loaded as an ENTITY by its natural key (ADR-0041 L2.4); the read-only guard reads the
/// resource's <c>OperationMode</c> (on assign from the loaded entity, on update/delete from the allocated
/// resource navigation, L2.5) and a soft-deleted/missing resource never blocks update/delete cleanup.
/// </summary>
public sealed class ResourceAssignmentEndpointTests
{
    private const string Provider = "acme";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";
    private const string Location = "gpu";

    // Slug supplied by the assign helpers (the use-case mints it from the resource name; these aggregate-built
    // fixtures pass it explicitly). PUT/DELETE address the allocation by this slug (ADR-0024).
    private const string AssignedSlug = "asg-res-1-test01";

    private const string ResName = "res-1";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

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

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static AssignResourceEndpoint.Request AssignFromWire(string resourceName, string allocationUnitType) =>
        JsonSerializer.Deserialize<AssignResourceEndpoint.Request>(
            JsonSerializer.Serialize(new
            {
                ResourceName = resourceName,
                AllocationUnitType = allocationUnitType,
                AllocationUnitCount = 100,
                Description = "d",
                AggregationName = "agg",
                Specifications = new[] { new { Name = "cpu", Value = "16" } },
            }),
            WireJson)!;

    private static UpdateAssignmentEndpoint.Request UpdateFromWire(string allocationUnitType) =>
        JsonSerializer.Deserialize<UpdateAssignmentEndpoint.Request>(
            JsonSerializer.Serialize(new
            {
                AllocationUnitType = allocationUnitType,
                AllocationUnitCount = 200,
                Description = "d2",
                AggregationName = "agg2",
                Specifications = new[] { new { Name = "cpu", Value = "32" } },
            }),
            WireJson)!;

    private const string PaddedProvider = "  AcMe  ";
    private const string MalformedProvider = " ";

    // A resource OWNED by the route provider (so invariant #5 passes), with the given read-only mode.
    private static ResourceAggregate ResourceUnderProvider(EOperationMode mode = EOperationMode.Editable, string name = ResName) =>
        ResourceAggregate.Create(ResourceName.From(name), ProviderForResource(), UserId.From(Guid.NewGuid()), Start, End, operationMode: mode);

    private static ProviderAggregate ProviderForResource() => ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);

    private static ProviderAggregate ProviderWithLocation(bool isShared = true)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: isShared);
        provider.ClearDomainEvents();
        return provider;
    }

    // Builds a provider with one active assignment of a resource carrying `resourceMode`; the AssignedLocation
    // navigation _resource therefore exposes that mode for the update/delete read-only guard (ADR-0041 L2.5).
    private static (ProviderAggregate Provider, string Slug) ProviderWithAssignment(EOperationMode resourceMode = EOperationMode.Editable)
    {
        var provider = ProviderWithLocation();
        var resource = ResourceAggregate.Create(ResourceName.From(ResName), provider, UserId.From(Guid.NewGuid()), Start, End, operationMode: resourceMode);
        provider.AssignResource(
            LocationName.From(Location), resource, allocationUnitTypeId: 2,
            allocationUnitCount: 100, description: "d", aggregationName: "agg", specifications: [], resourceHasNonSharedLocation: false,
            slug: AssignedSlug);
        var slug = provider.Locations.Single().AssignedLocations.Single().Slug!;
        provider.ClearDomainEvents();
        return (provider, slug);
    }

    private static (ProviderAggregate Provider, string Slug) ProviderWithDeletedAssignment()
    {
        var (provider, slug) = ProviderWithAssignment();
        provider.RemoveAssignment(LocationName.From(Location), slug);
        provider.ClearDomainEvents();
        return (provider, slug);
    }

    private static Mock<IResourceAssignmentQuery> AssignmentQuery(bool allocationTypeExists, bool hasNonSharedLocation = false)
    {
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allocationTypeExists);
        query.Setup(q => q.HasNonSharedLocationAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasNonSharedLocation);
        return query;
    }

    private static Mock<IProviderRepository> Repo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        // Update/Remove-assignment load via the resource-bearing shape (W1); assign uses the lean FindAsync.
        repo.Setup(r => r.FindWithAllocatedResourcesAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> ResourceRepo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Task<IResult> InvokePostAsync(
        Caller caller, ProviderAggregate? provider, ResourceAggregate? resource,
        bool allocationTypeExists, Mock<IUnitOfWork> unitOfWork, bool hasNonSharedLocation = false, string resourceName = ResName)
    {
        var request = AssignFromWire(resourceName, nameof(EAllocationUnitType.NodeHours));

        return AssignResourceEndpoint.HandleAsync(
            Provider, Location, request, caller, Repo(provider).Object, ResourceRepo(resource).Object,
            AssignmentQuery(allocationTypeExists, hasNonSharedLocation).Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(), NullLogger<AssignResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnCreatedWithAssignmentId_When_AssignValid()
    {
        var provider = ProviderWithLocation();
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePostAsync(AuthorizedCaller(), provider, ResourceUnderProvider(), allocationTypeExists: true, unitOfWork);

        var created = Assert.IsType<Created<AssignResourceEndpoint.Response>>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var response = created.Value!;
        // The use-case minted the slug from the resource name ("res-1") and set it on the allocation.
        Assert.Equal(provider.Locations.Single().AssignedLocations.Single().Slug, response.Slug);
        Assert.StartsWith("asg-res-1-", response.Slug);
        // Location points at the allocation's own slug URL, not the (renamed) resource route — guards the
        // GetResourceByIdV2 dead-route regression, which only surfaced at response-execution time.
        Assert.Equal($"v2/Providers/{Provider}/Locations/{Location}/Assignments/{response.Slug}", created.Location);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_LoadResourceByKey_When_Assigning()
    {
        // Phase D / ADR-0041 L2.4: the endpoint loads the resource entity by its per-provider natural key —
        // the resource is resolved only as owned by THIS provider, not globally by name.
        var provider = ProviderWithLocation();
        var resourceRepo = ResourceRepo(ResourceUnderProvider());

        var request = AssignFromWire(ResName, nameof(EAllocationUnitType.NodeHours));

        var result = await AssignResourceEndpoint.HandleAsync(
            Provider, Location, request, AuthorizedCaller(), Repo(provider).Object, resourceRepo.Object,
            AssignmentQuery(allocationTypeExists: true).Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(), NullLogger<AssignResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.IsType<Created<AssignResourceEndpoint.Response>>(result);
        resourceRepo.Verify(r => r.FindAsync(
            It.Is<ProjectResourceKey>(k => k.Provider.Value == Provider && k.Name.Value == ResName), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_NotAuthorized()
    {
        var result = await InvokePostAsync(UnauthorizedCaller(), ProviderWithLocation(), ResourceUnderProvider(), allocationTypeExists: true, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_ProviderMissing()
    {
        var result = await InvokePostAsync(AuthorizedCaller(), provider: null, ResourceUnderProvider(), allocationTypeExists: true, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_ResourceMissing()
    {
        var result = await InvokePostAsync(AuthorizedCaller(), ProviderWithLocation(), resource: null, allocationTypeExists: true, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.resource_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnUnprocessableAndNotSave_When_ResourceReadOnly()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePostAsync(AuthorizedCaller(), ProviderWithLocation(), ResourceUnderProvider(EOperationMode.ReadOnly), allocationTypeExists: true, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnUnprocessableWithCode_When_AllocationUnitTypeMissing()
    {
        var result = await InvokePostAsync(AuthorizedCaller(), ProviderWithLocation(), ResourceUnderProvider(), allocationTypeExists: false, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal("provider.allocation_unit_type_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnBadRequestAndNotSave_When_ResourceAlreadyHasNonSharedLocation()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePostAsync(AuthorizedCaller(), ProviderWithLocation(), ResourceUnderProvider(), allocationTypeExists: true, unitOfWork, hasNonSharedLocation: true);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationWithRequiredCode_When_ResourceNameEmpty()
    {
        var result = await InvokePostAsync(AuthorizedCaller(), ProviderWithLocation(), ResourceUnderProvider(), allocationTypeExists: true, SavingUnitOfWork(), resourceName: "");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.Required, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Resource name is required.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_StoreTheEnumValue_When_AssignNamesAnAllocationUnitType()
    {
        var provider = ProviderWithLocation();
        var query = AssignmentQuery(allocationTypeExists: true);

        var result = await AssignResourceEndpoint.HandleAsync(
            Provider, Location, AssignFromWire(ResName, "CoreHours"), AuthorizedCaller(), Repo(provider).Object,
            ResourceRepo(ResourceUnderProvider()).Object, query.Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(), NullLogger<AssignResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.IsType<Created<AssignResourceEndpoint.Response>>(result);
        query.Verify(q => q.AllocationUnitTypeExistsAsync((int)EAllocationUnitType.CoreHours, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((int)EAllocationUnitType.CoreHours, provider.Locations.Single().AssignedLocations.Single().AllocationUnitTypeId);
    }

    [Fact]
    public async Task Should_ReturnUnprocessable_When_AssignNamesNone()
    {
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => id is >= 1 and <= 6);
        var unitOfWork = SavingUnitOfWork();

        var result = await AssignResourceEndpoint.HandleAsync(
            Provider, Location, AssignFromWire(ResName, "None"), AuthorizedCaller(), Repo(ProviderWithLocation()).Object,
            ResourceRepo(ResourceUnderProvider()).Object, query.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(), NullLogger<AssignResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal("provider.allocation_unit_type_not_found", problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Should_FailBinding_When_AssignNamesAnUnknownAllocationUnitType()
    {
        Assert.Throws<JsonException>(() => AssignFromWire(ResName, "NoSuchUnit"));
    }

    [Fact]
    public void Should_FailBinding_When_UpdateNamesAnUnknownAllocationUnitType()
    {
        Assert.Throws<JsonException>(() => UpdateFromWire("NoSuchUnit"));
    }

    [Fact]
    public async Task Should_StoreTheEnumValue_When_UpdateNamesAnAllocationUnitType()
    {
        var (provider, slug) = ProviderWithAssignment();
        var query = AssignmentQuery(allocationTypeExists: true);

        var result = await UpdateAssignmentEndpoint.HandleAsync(
            Provider, Location, slug, UpdateFromWire("QPUSeconds"), AuthorizedCaller(), Repo(provider).Object,
            query.Object, Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateAssignmentEndpoint.AuditCategory>(), NullLogger<UpdateAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        query.Verify(q => q.AllocationUnitTypeExistsAsync((int)EAllocationUnitType.QPUSeconds, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((int)EAllocationUnitType.QPUSeconds, provider.Locations.Single().AssignedLocations.Single().AllocationUnitTypeId);
    }

    private static Task<IResult> InvokePutAsync(
        Caller caller, ProviderAggregate? provider, string slug,
        bool allocationTypeExists, Mock<IUnitOfWork> unitOfWork)
    {
        var request = UpdateFromWire(nameof(EAllocationUnitType.CoreHours));

        return UpdateAssignmentEndpoint.HandleAsync(
            Provider, Location, slug, request, caller, Repo(provider).Object, AssignmentQuery(allocationTypeExists).Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<UpdateAssignmentEndpoint.AuditCategory>(), NullLogger<UpdateAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_UpdateValid()
    {
        var (provider, id) = ProviderWithAssignment();
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePutAsync(AuthorizedCaller(), provider, id, allocationTypeExists: true, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnUnprocessable_When_UpdateAllocationUnitTypeMissing()
    {
        var (provider, id) = ProviderWithAssignment();
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePutAsync(AuthorizedCaller(), provider, id, allocationTypeExists: false, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal("provider.allocation_unit_type_not_found", problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_UpdateAssignmentMissing()
    {
        var provider = ProviderWithLocation();

        var result = await InvokePutAsync(AuthorizedCaller(), provider, "asg-missing-000000", allocationTypeExists: true, SavingUnitOfWork());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_UpdateAssignmentAlreadyDeleted()
    {
        var (provider, id) = ProviderWithDeletedAssignment();

        var result = await InvokePutAsync(AuthorizedCaller(), provider, id, allocationTypeExists: true, SavingUnitOfWork());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnUnprocessableAndNotSave_When_UpdateResourceReadOnly()
    {
        var (provider, id) = ProviderWithAssignment(EOperationMode.ReadOnly);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePutAsync(AuthorizedCaller(), provider, id, allocationTypeExists: true, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_UpdateAndResourceEditable()
    {
        // The allocated resource is editable, so the cross-aggregate read-only guard does not block.
        var (provider, id) = ProviderWithAssignment(EOperationMode.Editable);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokePutAsync(AuthorizedCaller(), provider, id, allocationTypeExists: true, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Task<IResult> InvokeDeleteAsync(
        Caller caller, ProviderAggregate? provider, string slug, Mock<IUnitOfWork> unitOfWork)
        => RemoveAssignmentEndpoint.HandleAsync(
            Provider, Location, slug, caller, Repo(provider).Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<RemoveAssignmentEndpoint.AuditCategory>(), NullLogger<RemoveAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);

    [Fact]
    public async Task Should_ReturnNoContent_When_RemoveValid()
    {
        var (provider, id) = ProviderWithAssignment();
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeDeleteAsync(AuthorizedCaller(), provider, id, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_RemoveAssignmentMissing()
    {
        var provider = ProviderWithLocation();

        var result = await InvokeDeleteAsync(AuthorizedCaller(), provider, "asg-missing-000000", SavingUnitOfWork());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_RemoveAssignmentAlreadyDeleted()
    {
        var (provider, id) = ProviderWithDeletedAssignment();

        var result = await InvokeDeleteAsync(AuthorizedCaller(), provider, id, SavingUnitOfWork());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnUnprocessableAndNotSave_When_RemoveResourceReadOnly()
    {
        var (provider, id) = ProviderWithAssignment(EOperationMode.ReadOnly);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeDeleteAsync(AuthorizedCaller(), provider, id, unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.ProviderReadOnly, problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_RemoveAndResourceEditable()
    {
        var (provider, id) = ProviderWithAssignment(EOperationMode.Editable);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeDeleteAsync(AuthorizedCaller(), provider, id, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_RemoveNotAuthorized()
    {
        var (provider, id) = ProviderWithAssignment();

        var result = await InvokeDeleteAsync(UnauthorizedCaller(), provider, id, SavingUnitOfWork());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    // ── Authorize-before-validate ordering (#261b) ───────────────────────────
    // Every assignment write authorizes the raw provider name before parsing it, so a malformed name never
    // leaks — via a 400 — that it reached validation: a non-admin sees 403, an authorized caller still sees the
    // 400. Owner equality survives the raw-string path (ADR-0012); admin still validates once authorized.

    private static Task<IResult> InvokeAssignRawAsync(Caller caller, string providerName) =>
        AssignResourceEndpoint.HandleAsync(
            providerName, Location,
            AssignFromWire(ResName, nameof(EAllocationUnitType.NodeHours)),
            caller, Repo(provider: null).Object, ResourceRepo(resource: null).Object,
            AssignmentQuery(allocationTypeExists: true).Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(), NullLogger<AssignResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeUpdateRawAsync(Caller caller, string providerName) =>
        UpdateAssignmentEndpoint.HandleAsync(
            providerName, Location, AssignedSlug,
            UpdateFromWire(nameof(EAllocationUnitType.CoreHours)),
            caller, Repo(provider: null).Object, AssignmentQuery(allocationTypeExists: true).Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateAssignmentEndpoint.AuditCategory>(), NullLogger<UpdateAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeRemoveRawAsync(Caller caller, string providerName) =>
        RemoveAssignmentEndpoint.HandleAsync(
            providerName, Location, AssignedSlug, caller, Repo(provider: null).Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<RemoveAssignmentEndpoint.AuditCategory>(), NullLogger<RemoveAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> Dispatch(string endpoint, Caller caller, string providerName) => endpoint switch
    {
        "AssignResource" => InvokeAssignRawAsync(caller, providerName),
        "UpdateAssignment" => InvokeUpdateRawAsync(caller, providerName),
        "RemoveAssignment" => InvokeRemoveRawAsync(caller, providerName),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
    };

    public static TheoryData<string> ReorderedEndpoints() => new()
    {
        "AssignResource", "UpdateAssignment", "RemoveAssignment",
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

        // Padded/differently-cased owner name authorizes and parses, so the flow reaches the (absent) provider
        // and returns 404 — never 403 (auth intact) or 400 (parsed after auth).
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }
}
