using System.Text.Json;

using Core.Domain.Shared;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Exceptions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Resources;

/// <summary>
/// Characterization tests for <see cref="FinalizeCreationProjectResourceHandler.HandleAsync"/>, written before the
/// T-197-07 extract-method refactor. They pin how the <c>Data</c> bag is read (array only, null names and zero ids
/// skipped, exact-case location match, first entry wins), the cluster access request per location, the exception a
/// missing HEAppE URI surfaces as, and the resource state update and save after every check passed.
/// </summary>
public sealed class FinalizeCreationProjectResourceHandlerCharacterizationTests
{
    private const string Location = "location-1";
    private const string Username = "user@example.com";
    private const string ResourceNameValue = "test-resource";
    private const long ProjectId = 42;

    private static readonly ProviderName TestProvider = ProviderName.From("test-provider");
    private static readonly DateTimeOffset StartDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndDate = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IHeappeService> _heappeService = new();
    private readonly Mock<IRepository<ResourceAggregate, ProjectResourceKey>> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IProjectCreationService> _projectCreation = new();
    private readonly List<(Uri BaseAddress, ClusterAvailabilityRequest Request)> _accessChecks = [];
    private readonly ResourceAggregate _resource = ResourceAggregate.Create(
        ResourceName.Create(ResourceNameValue), Core.Domain.Provider.Provider.Create(TestProvider, countryId: 1), UserId.From(Guid.NewGuid()),
        StartDate, EndDate,
        description: "old",
        primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));

    private static readonly IReadOnlyList<ResolvedLocationRef> _resolvedLocations = new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        };

    public FinalizeCreationProjectResourceHandlerCharacterizationTests()
    {
        _heappeService
            .Setup(h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BaseHeappeConnectionInfo, ClusterAvailabilityRequest, CancellationToken>((c, r, _) => _accessChecks.Add((c.BaseAddress, r)))
            .ReturnsAsync(Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new(ClusterName: "c1", IsClusterAccessible: true) }));
        _repository
            .Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(_resource));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));

        _projectCreation
           .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
               It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
               new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier("develop"))));
    }

    private FinalizeCreationProjectResourceHandler Handler() => new(_heappeService.Object, _repository.Object, _unitOfWork.Object, _projectCreation.Object);

    private static LocationAssignment Assignment(string locationName = Location, string heappeUri = "https://heappe.example.org/") => new(
        LocationName: locationName,
        AllocationUnitType: EAllocationUnitType.NodeHours,
        AggregationName: "agg",
        AllocationUnitCount: 100,
        AllocationDescription: "alloc",
        PreferredAuthenticationType: "Password",
        Specifications: [new(ApplicationConstants.HeappeUriKey, heappeUri), new(ApplicationConstants.HeappeIdentifierKey, "develop")]);

    private static FinalizeCreationProjectResourceStep Step(List<LocationAssignment> assignments, string? json) => new(
        RequestId: Guid.NewGuid(),
        ProviderName: TestProvider,
        Name: ResourceNameValue,
        PrimaryInvestigatorEmail: "pi@example.com",
        AssignedLocations: assignments,
        ResolvedLocations: _resolvedLocations,
        Data: json is null ? null : JsonDocument.Parse(json));

    [Fact]
    public async Task Should_CheckClusterAccessWithTheProjectIdAndUsernameAtTheNormalizedUri_ThenAdvanceTheStateAndSave()
    {
        var result = await Handler().HandleAsync(
            Step([Assignment()], """[{"LocationName":"location-1","ProjectId":42,"Username":"user@example.com"}]"""), CancellationToken.None);

        Assert.Null(result.Data);
        var check = Assert.Single(_accessChecks);
        Assert.Equal(new Uri("https://heappe.example.org"), check.BaseAddress);
        Assert.Equal(new ClusterAvailabilityRequest(42, Username), check.Request);
        Assert.Equal(EProjectResourceState.FinalizeCreationProjectResourceStep, _resource.State);
        _repository.Verify(
            r => r.FindAsync(new ProjectResourceKey(TestProvider, ResourceName.Create(ResourceNameValue)), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_UseTheFirstDataEntry_When_ALocationAppearsTwice()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
           () => Handler().HandleAsync(
            Step([Assignment()], """[{"LocationName":"location-1","ProjectId":7},{"LocationName":"location-1","ProjectId":8}]"""),
            CancellationToken.None));
    }

    [Fact]
    public async Task Should_ThrowOnAMissingPropertyInADataEntry()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Handler().HandleAsync(Step([Assignment()], """[{"LocationName":"location-1"}]"""), CancellationToken.None));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_FailPermanently_When_AnAssignmentWithAProjectLacksTheHeappeIdentifier()
    {
        var assignment = Assignment() with { Specifications = [new(ApplicationConstants.HeappeUriKey, "https://heappe.example.org/")] };

        var ex = await Assert.ThrowsAsync<PermanentException>(
            () => Handler().HandleAsync(Step([assignment], """[{"LocationName":"location-1","ProjectId":42}]"""), CancellationToken.None));

        Assert.Equal("HEAPPE_IDENTIFIER_INVALID", ex.ErrorInfo.ErrorCode);
        Assert.Empty(_accessChecks);
    }

    [Fact]
    public async Task Should_NameTheInaccessibleClusterAndNotSave_When_AnyClusterIsInaccessible()
    {
        _heappeService
            .Setup(h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new("ok", true), new("barbora", false), new("karolina", false) }));

        var ex = await Assert.ThrowsAsync<RetryableException>(
            () => Handler().HandleAsync(Step([Assignment()], """[{"LocationName":"location-1","ProjectId":42}]"""), CancellationToken.None));

        Assert.Equal("CLUSTER_ACCESS_FAILED", ex.ErrorInfo.ErrorCode);
        Assert.Equal("Cluster barbora is not accessible", ex.Message);
        Assert.Equal(EProjectResourceState.Draft, _resource.State);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_CarryTheHeappeErrorDescriptionAndStopAtTheFirstFailingLocation_When_TheAccessCallFails()
    {
        _heappeService
            .Setup(h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BaseHeappeConnectionInfo, ClusterAvailabilityRequest, CancellationToken>((c, r, _) => _accessChecks.Add((c.BaseAddress, r)))
            .ReturnsAsync(Result<IList<ClusterAvailabilityModel>, Error>.Failure(
                new ErrorBuilder().WithDescription("heappe down").WithSeverity(ErrorSeverity.Error).Build()));

        var ex = await Assert.ThrowsAsync<RetryableException>(() => Handler().HandleAsync(
            Step([Assignment(), Assignment("location-2")],
            """[{"LocationName":"location-1","ProjectId":1},{"LocationName":"location-2","ProjectId":2}]"""),
            CancellationToken.None));

        Assert.Equal("CLUSTER_ACCESS_FAILED", ex.ErrorInfo.ErrorCode);
        Assert.Equal("heappe down", ex.Message);
        Assert.Single(_accessChecks);
    }
}
