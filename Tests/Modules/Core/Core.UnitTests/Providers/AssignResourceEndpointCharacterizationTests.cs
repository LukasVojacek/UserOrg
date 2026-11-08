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
/// Characterization tests for <see cref="AssignResourceEndpoint.HandleAsync"/>, written before the T-197-07
/// extract-method refactor. They pin the malformed location name guard, the order of the input guards and of the
/// read-only, allocation unit type and domain checks, the arguments the cross-aggregate queries receive, and that a
/// domain failure neither marks the allocation added nor saves.
/// </summary>
public sealed class AssignResourceEndpointCharacterizationTests
{
    private const string Provider = "acme";
    private const string Location = "gpu";
    private const string ResName = "res-1";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IProviderRepository> _providers = new();
    private readonly Mock<IRepository<ResourceAggregate, ProjectResourceKey>> _resources = new();
    private readonly Mock<IResourceAssignmentQuery> _query = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<string> _calls = [];

    public AssignResourceEndpointCharacterizationTests()
    {
        _providers.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ProviderAggregate?>(ProviderWithLocation()));
        _providers.Setup(r => r.MarkAssignedLocationAsAdded(It.IsAny<AssignedLocation>()))
            .Callback(() => _calls.Add("mark"));
        _resources.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(Resource()));
        _query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _query.Setup(q => q.HasNonSharedLocationAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("save"))
            .ReturnsAsync(1);
    }

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProviderAggregate ProviderWithLocation(bool isShared = true)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: isShared);
        provider.ClearDomainEvents();
        return provider;
    }

    private static ResourceAggregate Resource(EOperationMode mode = EOperationMode.Editable) =>
        ResourceAggregate.Create(ResourceName.From(ResName), ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1), UserId.From(Guid.NewGuid()), Start, End, operationMode: mode);

    private static AssignResourceEndpoint.Request Request(string resourceName = ResName) =>
        new(resourceName, EAllocationUnitType.CoreHours, AllocationUnitCount: 10, Description: "d", AggregationName: "agg", Specifications: null);

    private Task<IResult> InvokeAsync(
        AssignResourceEndpoint.Request? request = null, Caller? caller = null, string providerName = Provider, string locationName = Location) =>
        AssignResourceEndpoint.HandleAsync(
            providerName, locationName, request ?? Request(), caller ?? OwnerCaller(),
            _providers.Object, _resources.Object, _query.Object,
            Options.Create(new WellKnownRoles()), _unitOfWork.Object, new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(), NullLogger<AssignResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static ProblemHttpResult AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        return problem;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_Return400InvalidLocationNameBeforeAnyLookup_When_TheLocationRouteValueIsBlank(string locationName)
    {
        var result = await InvokeAsync(Request(resourceName: ""), locationName: locationName);

        var problem = AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
        Assert.Equal("LocationName cannot be empty", problem.ProblemDetails.Detail);
        _providers.Verify(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReportTheProviderNameBeforeTheLocationName_When_AnAdminSendsBothMalformed()
    {
        var result = await InvokeAsync(caller: AdminCaller(), providerName: " ", locationName: " ");

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidName);
    }

    [Fact]
    public async Task Should_NotLoadTheResource_When_TheProviderIsMissing()
    {
        _providers.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ProviderAggregate?>(null));

        var result = await InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "provider.not_found");
        _resources.Verify(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReportReadOnlyBeforeTheUnitType_When_BothFail()
    {
        _resources.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(Resource(EOperationMode.ReadOnly)));
        _query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await InvokeAsync();

        var problem = AssertProblem(result, StatusCodes.Status422UnprocessableEntity, ProviderErrorCodes.ProviderReadOnly);
        Assert.Equal("Project resource 'res-1' is in read-only mode and cannot be modified.", problem.ProblemDetails.Detail);
        _query.Verify(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_NotQueryTheNonSharedFlag_When_TheUnitTypeIsUnknown()
    {
        _query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await InvokeAsync();

        var problem = AssertProblem(result, StatusCodes.Status422UnprocessableEntity, "provider.allocation_unit_type_not_found");
        Assert.Equal("Allocation unit type 'CoreHours' does not exist.", problem.ProblemDetails.Detail);
        _query.Verify(q => q.HasNonSharedLocationAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_QueryTheUnitTypeByIdAndTheNonSharedFlagByNaturalKey()
    {
        await InvokeAsync();

        _query.Verify(q => q.AllocationUnitTypeExistsAsync((int)EAllocationUnitType.CoreHours, It.IsAny<CancellationToken>()), Times.Once);
        _query.Verify(q => q.HasNonSharedLocationAsync(
            new ProjectResourceKey(ProviderName.From(Provider), ResourceName.From(ResName)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_MarkTheAllocationAddedBeforeSaving_When_TheAssignmentSucceeds()
    {
        var result = await InvokeAsync();

        Assert.IsType<Created<AssignResourceEndpoint.Response>>(result);
        Assert.Equal(["mark", "save"], _calls);
    }

    [Fact]
    public async Task Should_NeitherMarkNorSave_When_TheDomainRejectsTheAssignment()
    {
        _query.Setup(q => q.HasNonSharedLocationAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _providers.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ProviderAggregate?>(ProviderWithLocation(isShared: false)));

        var result = await InvokeAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Empty(_calls);
    }
}
