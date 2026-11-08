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
using System.Text.Json;
using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;
using WorkflowEngine.Contracts.Exceptions;
using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Resources;

/// <summary>
/// Pins the behaviour of <see cref="FinalizeCreationProjectResourceHandler"/>: it resolves each assigned location's
/// HEAppE project id from the hand-written <c>Data</c> JSON bag written upstream by <see cref="InitializeHeappeHandler"/>
/// (locationName + projectId), calls <see cref="IHeappeService.TestClusterAccessForAccountAsync"/> once per assigned
/// location, throws <see cref="PermanentException"/> HEAPPE_PROJECT_NOT_FOUND when a location has no matching project id,
/// throws <see cref="RetryableException"/> CLUSTER_ACCESS_FAILED both when the HEAppE call itself fails and when it
/// succeeds but reports a cluster as inaccessible, and otherwise returns <c>StepResult(null)</c>.
/// </summary>
public sealed class FinalizeCreationProjectResourceHandlerTests
{
    private const string Location = "location-1";
    private const string OtherLocation = "location-2";
    private const string HeappeUri = "https://heappe.example.org";
    private const long ProjectId = 42;
    private const long OtherProjectId = 43;
    private static DateTimeOffset StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset EndDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static LocationAssignment AssignedLocation(string locationName = Location) => new(
        LocationName: locationName,
        AllocationUnitType: EAllocationUnitType.NodeHours,
        AggregationName: "agg",
        AllocationUnitCount: 100,
        AllocationDescription: "alloc",
        PreferredAuthenticationType: "Password",
        Specifications: new List<SpecificationItem>
        {
            new(ApplicationConstants.HeappeUriKey, HeappeUri),
            new(ApplicationConstants.HeappeIdentifierKey, "develop"),
        });

    private static readonly IReadOnlyList<ResolvedLocationRef> _resolvedLocations = new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint"),
            new(LocationName: "location-2", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        };

    private static JsonDocument DataBag(params (string LocationName, long ProjectId)[] entries)
    {
        var jsonArray = entries.Select(e => new { locationName = e.LocationName, projectId = e.ProjectId }).ToList();
        return JsonDocument.Parse(JsonSerializer.Serialize(jsonArray));
    }

    private static FinalizeCreationProjectResourceStep Step(
        List<LocationAssignment> assignedLocations, JsonDocument? data, Provider provider) => new(
        RequestId: Guid.NewGuid(),
        ProviderName: provider.Name,
        Name: "test-resource",
        PrimaryInvestigatorEmail: "pi@example.com",
        AssignedLocations: assignedLocations,
        ResolvedLocations: _resolvedLocations,
        Data: data);

    private static Mock<IHeappeService> HeappeServiceReturning(
        Result<IList<ClusterAvailabilityModel>, Error> result) =>
        HeappeServiceReturning((_, _) => result);

    private static Mock<IHeappeService> HeappeServiceReturning(
        Func<BaseHeappeConnectionInfo, ClusterAvailabilityRequest, Result<IList<ClusterAvailabilityModel>, Error>> resultFor)
    {
        var heappeService = new Mock<IHeappeService>();
        heappeService
            .Setup(h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BaseHeappeConnectionInfo conn, ClusterAvailabilityRequest req, CancellationToken _) => resultFor(conn, req));
        return heappeService;
    }

    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
      ResourceName.Create("test-resource"), Core.Domain.Provider.Provider.Create(ProviderName.From("test-provider"), countryId: 1), UserId.From(Guid.NewGuid()),
      StartDate, EndDate,
      description: "old",
      primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> Repo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    [Fact]
    public async Task Should_ReturnNullStepResult_When_AllAssignedLocationsHaveAccessibleClusters()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));
        projectCreation
           .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
               It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
               new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier("develop"))));

        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);
        var unitOfWork = SavingUnitOfWork();
        var heappeService = HeappeServiceReturning(
            Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new(ClusterName: Location, IsClusterAccessible: true) }));

        var step = Step(
            new List<LocationAssignment> { AssignedLocation() },
            DataBag((Location, ProjectId)),
            provider);

        var result = await new FinalizeCreationProjectResourceHandler(heappeService.Object, Repo(ExistingResource()).Object, unitOfWork.Object, projectCreation.Object).HandleAsync(step, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Data);
        heappeService.Verify(
            h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_CallClusterAccessCheckOncePerAssignedLocation_When_MultipleLocations()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));
        projectCreation
           .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
               It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
               new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier("develop"))));

        var calledFor = new List<long>();
        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);
        var unitOfWork = SavingUnitOfWork();
        var heappeService = HeappeServiceReturning((_, req) =>
        {
            calledFor.Add(req.ProjectId);
            return Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new(ClusterName: "any", IsClusterAccessible: true) });
        });

        var step = Step(
            new List<LocationAssignment> { AssignedLocation(Location), AssignedLocation(OtherLocation) },
            DataBag((Location, ProjectId), (OtherLocation, OtherProjectId)),
            provider);

        var result = await new FinalizeCreationProjectResourceHandler(heappeService.Object, Repo(ExistingResource()).Object, unitOfWork.Object, projectCreation.Object).HandleAsync(step, CancellationToken.None);

        Assert.NotNull(result);
        heappeService.Verify(
            h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        Assert.Equal(new[] { ProjectId, OtherProjectId }, calledFor);
    }

    [Fact]
    public async Task Should_ThrowPermanentExceptionWithProjectNotFoundCode_When_DataBagHasNoMatchingLocation()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));

        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);
        var unitOfWork = SavingUnitOfWork();

        var heappeService = HeappeServiceReturning(
            Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new(ClusterName: Location, IsClusterAccessible: true) }));

        var step = Step(
            new List<LocationAssignment> { AssignedLocation() },
            DataBag((OtherLocation, ProjectId)),
            provider); // no entry for Location

        // Handler skips locations without matching project id in DataBag (projectId == 0).
        // It then fails to find the resource, throwing RESOURCE_NOT_FOUND when repo returns null.
        var ex = await Assert.ThrowsAsync<PermanentException>(
            () => new FinalizeCreationProjectResourceHandler(heappeService.Object, Repo(null).Object, unitOfWork.Object, projectCreation.Object).HandleAsync(step, CancellationToken.None));

        Assert.Equal("RESOURCE_NOT_FOUND", ex.ErrorInfo.ErrorCode);
        heappeService.Verify(
            h => h.TestClusterAccessForAccountAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<ClusterAvailabilityRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ThrowPermanentExceptionWithProjectNotFoundCode_When_DataIsNull()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));

        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);
        var unitOfWork = SavingUnitOfWork();

        var heappeService = HeappeServiceReturning(
            Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new(ClusterName: Location, IsClusterAccessible: true) }));

        var step = Step(new List<LocationAssignment> { AssignedLocation() }, data: null, provider: provider);

        // When data is null, handler skips all location HEAppE checks (no heappeProjects to iterate).
        // It then fails to find the resource, throwing RESOURCE_NOT_FOUND when repo returns null.
        var ex = await Assert.ThrowsAsync<PermanentException>(
            () => new FinalizeCreationProjectResourceHandler(heappeService.Object, Repo(null).Object, unitOfWork.Object, projectCreation.Object).HandleAsync(step, CancellationToken.None));

        Assert.Equal("RESOURCE_NOT_FOUND", ex.ErrorInfo.ErrorCode);
    }

    [Fact]
    public async Task Should_ThrowRetryableExceptionWithClusterAccessFailedCode_When_HeappeCallFails()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));
        projectCreation
           .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
               It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
               new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier("develop"))));

        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);
        var unitOfWork = SavingUnitOfWork();

        var error = new ErrorBuilder().WithDescription("boom").WithSeverity(ErrorSeverity.Error).Build();
        var heappeService = HeappeServiceReturning(
            Result<IList<ClusterAvailabilityModel>, Error>.Failure(error));

        var step = Step(
            new List<LocationAssignment> { AssignedLocation() },
            DataBag((Location, ProjectId)),
            provider);

        var ex = await Assert.ThrowsAsync<RetryableException>(
            () => new FinalizeCreationProjectResourceHandler(heappeService.Object, Repo(ExistingResource()).Object, unitOfWork.Object, projectCreation.Object).HandleAsync(step, CancellationToken.None));

        Assert.Equal("CLUSTER_ACCESS_FAILED", ex.ErrorInfo.ErrorCode);
    }

    [Fact]
    public async Task Should_ThrowRetryableExceptionWithClusterAccessFailedCode_When_ClusterReportedInaccessible()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));
        projectCreation
           .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
               It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
               It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
               new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier("develop"))));

        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);
        var unitOfWork = SavingUnitOfWork();

        var heappeService = HeappeServiceReturning(
            Result<IList<ClusterAvailabilityModel>, Error>.Success(
                new List<ClusterAvailabilityModel> { new(ClusterName: Location, IsClusterAccessible: false) }));

        var step = Step(
            new List<LocationAssignment> { AssignedLocation() },
            DataBag((Location, ProjectId)),
            provider);

        var ex = await Assert.ThrowsAsync<RetryableException>(
            () => new FinalizeCreationProjectResourceHandler(heappeService.Object, Repo(ExistingResource()).Object, unitOfWork.Object, projectCreation.Object).HandleAsync(step, CancellationToken.None));

        Assert.Equal("CLUSTER_ACCESS_FAILED", ex.ErrorInfo.ErrorCode);
    }
}
