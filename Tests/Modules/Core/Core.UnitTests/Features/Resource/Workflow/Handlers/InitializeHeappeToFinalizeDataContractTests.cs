using Core.Domain.Shared;
using Core.Features.Request.Notifications;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;
using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;
using Lukdrasil.Error;
using Lukdrasil.Result;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;
using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Pins the JSON <c>Data</c> data-contract ROUND-TRIP between the two workflow steps: the array of
/// hand-serialized <c>{ locationName, projectId, publicKey }</c> entries that
/// <see cref="InitializeHeappeHandler"/> WRITES into <see cref="WorkflowEngine.Contracts.Abstractions.StepResult.Data"/>
/// is exactly what <see cref="FinalizeCreationProjectResourceHandler"/> hand-parses back out (it reads
/// <c>locationName</c> + <c>projectId</c>). The two handlers agree only on stringly-typed keys, so this test
/// drives InitializeHeappe's real output straight into Finalize: a rename of either key (e.g. <c>projectId</c> →
/// <c>projectID</c>) makes Finalize fail to resolve the project id and throw HEAPPE_PROJECT_NOT_FOUND, breaking
/// this test. The individual WRITE shape is pinned by <see cref="InitializeHeappeHandlerTests"/>; this pins the
/// READ side and the contract between them.
/// </summary>
public sealed class InitializeHeappeToFinalizeDataContractTests
{
    private const string Location = "location-1";
    private const string HeappeUri = "https://heappe.example.org";
    private const string HeappeIdentifier = "heappe-id-1";
    private const long ProjectId = 42;
    private static DateTimeOffset StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset EndDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static LocationAssignment AssignedLocation() => new(
        LocationName: Location,
        AllocationUnitType: EAllocationUnitType.NodeHours,
        AggregationName: "agg",
        AllocationUnitCount: 100,
        AllocationDescription: "alloc",
        PreferredAuthenticationType: "Password",
        Specifications: new List<SpecificationItem>
        {
            new(ApplicationConstants.HeappeUriKey, HeappeUri),
            new(ApplicationConstants.HeappeIdentifierKey, HeappeIdentifier),
        });

    private static InitializeHeappeStep InitStep(List<LocationAssignment> assignedLocations, Provider provider) => new(
        RequestId: Guid.NewGuid(),
        provider.Name,
        new Request(
        Name: "test-resource",
        Description: "desc",
        PrimaryInvestigatorEmail: "pi@example.com",
        StartDate: StartDate,
        EndDate: EndDate,
        AccountString: "ACC-1",
        UnitType: EAllocationUnitType.NodeHours,
        MappingOneToOne: true,
        UseAccountingStringForScheduler: false,
        Usernames: new List<string>() { "user@example.com" },
        LicenseUrl: "https://example.com/license",
        BypassHeappe: false,
        Clusters: new List<Cluster> { new(ClusterName: Location, ScratchPath: "/scratch", ProjectPath: "/project") },
        AssignedLocations: assignedLocations),
        ResolvedLocations: new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        });

    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
     ResourceName.Create("test-resource"), Core.Domain.Provider.Provider.Create(ProviderName.From("test-provider"), countryId: 1),
     UserId.From(Guid.NewGuid()),
     StartDate, EndDate,
     description: "old",
     primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));

    private static InitializeHeappeHandler CreateInitializeHandler()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        var heappeService = new Mock<IHeappeService>();
        var providerRepository = new Mock<IProviderRepository>();

        heappeService
            .Setup(h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(
                new List<ClusterAuthenticationCredentialAuthType> { new() { Id = 11, Name = "Password" } }));

        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(ProjectId));

        projectCreation
            .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
                It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
                new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier(HeappeIdentifier))));

        var connectionFactory = new Mock<IHeappeConnectionFactory>();
        connectionFactory
            .Setup(f => f.ForPublicAsync(It.IsAny<UserOrg.Shared.HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PublicHeappeConnectionInfo, Error>.Success(
                new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier(HeappeIdentifier))));

        heappeService
            .Setup(h => h.GenerateSecureShellKeyAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<SecureShellKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<SecureShellKeyModel>, Error>.Success(
                new List<SecureShellKeyModel> { new("user@example.com", 0, "ssh-rsa AAAA...", "-----BEGIN...") }));

        providerRepository
            .Setup(p => p.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Provider.Create(
                ProviderName.From("test-provider"),
                countryId: 1,
                supportEmail: Email.Create("support@provider.test")));

        return new InitializeHeappeHandler(new Mock<ILogger<InitializeHeappeHandler>>().Object, projectCreation.Object,
            heappeService.Object, connectionFactory.Object, Repo(ExistingResource()).Object, SavingUnitOfWork().Object,
            new Mock<INotificationSender>().Object, providerRepository.Object);
    }

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
}
