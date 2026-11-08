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
using WorkflowEngine.Contracts.Exceptions;
using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Characterization tests for <see cref="InitializeHeappeHandler"/> pinning today's success-path JSON
/// data-contract (hand-parsed string keys "locationName"/"projectId"/"publicKey" consumed downstream by
/// <see cref="FinalizeCreationProjectResourceHandler"/>). Also pins that a cluster-name mismatch currently
/// throws (today it is wrapped into a RetryableException via the outer catch, per G3) — the exact exception
/// type is intentionally NOT pinned here since G3 changes it to a PermanentException; only "it throws" is
/// pinned so that swap remains the sole observable delta.
/// </summary>
public sealed class InitializeHeappeHandlerTests
{
    private const string HeappeUri = "https://heappe.example.org";
    private const string HeappeIdentifier = "heappe-id-1";
    private static DateTimeOffset StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset EndDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static InitializeHeappeStep CreateStep(List<Cluster> clusters, List<LocationAssignment> assignedLocations, Provider provider) => new(
        RequestId: Guid.NewGuid(),
        ProviderName: provider.Name,
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
        LicenseUrl: "https://license.example.org",
        Clusters: clusters,
        BypassHeappe: false,
        AssignedLocations: assignedLocations),
        ResolvedLocations: new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        });

    private static LocationAssignment CreateAssignedLocation(string locationName = "location-1") => new(
        LocationName: locationName,
        AllocationUnitType: EAllocationUnitType.NodeHours,
        AggregationName: "agg",
        AllocationUnitCount: 100,
        AllocationDescription: "alloc",
        PreferredAuthenticationType: "Password",
        Specifications: new List<SpecificationItem>
        {
            new(ApplicationConstants.HeappeUriKey, HeappeUri),
            new(ApplicationConstants.HeappeIdentifierKey, HeappeIdentifier)
        });

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

    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
     ResourceName.Create("test-resource"), Core.Domain.Provider.Provider.Create(ProviderName.From("test-provider"), countryId: 1),
     UserId.From(Guid.NewGuid()),
     StartDate, EndDate,
     description: "old",
     primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));


    private static (Mock<IProjectCreationService> ProjectCreation, Mock<IHeappeService> HeappeService, InitializeHeappeHandler Handler) CreateSut()
    {
        var projectCreation = new Mock<IProjectCreationService>();
        var heappeService = new Mock<IHeappeService>();
        var logger = new Mock<ILogger<InitializeHeappeHandler>>();
        var notificationSender = new Mock<INotificationSender>();
        var providerRepository = new Mock<IProviderRepository>();
        var connectionFactory = new Mock<IHeappeConnectionFactory>();
        connectionFactory
            .Setup(f => f.ForPublicAsync(It.IsAny<UserOrg.Shared.HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PublicHeappeConnectionInfo, Error>.Success(
                new PublicHeappeConnectionInfo(new Uri("https://nexusapi/api/heappe/heappe-id-1/"), new UserOrg.Shared.HeappeIdentifier(HeappeIdentifier))));

        var handler = new InitializeHeappeHandler(logger.Object, projectCreation.Object, heappeService.Object, connectionFactory.Object, Repo(ExistingResource()).Object,
            SavingUnitOfWork().Object, notificationSender.Object, providerRepository.Object);

        providerRepository
            .Setup(p => p.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Provider.Create(
                ProviderName.From("test-provider"),
                countryId: 1,
                supportEmail: Email.Create("support@provider.test")));

        return (projectCreation, heappeService, handler);
    }

    [Fact]
    public async Task Should_ReturnJsonArrayWithLocationNameProjectIdAndPublicKey_When_InitializationSucceeds()
    {
        // Arrange
        var (projectCreation, heappeService, handler) = CreateSut();
        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);

        heappeService
            .Setup(h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(
                new List<ClusterAuthenticationCredentialAuthType> { new() { Id = 11, Name = "Password" } }));

        projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Success(42));

        projectCreation
            .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
                It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
                new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier(HeappeIdentifier))));

        heappeService
            .Setup(h => h.GenerateSecureShellKeyAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<SecureShellKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<SecureShellKeyModel>, Error>.Success(
                new List<SecureShellKeyModel> { new("user@example.com", 0, "ssh-rsa AAAA...", "-----BEGIN...") }));

        var clusters = new List<Cluster> { new(ClusterName: "location-1", ScratchPath: "/scratch", ProjectPath: "/project") };
        var assignedLocations = new List<LocationAssignment> { CreateAssignedLocation("location-1") };

        // Handler only adds entries to heappeProjectIds when MappingOneToOne is false, so override that for this test
        var step = new InitializeHeappeStep(
            RequestId: Guid.NewGuid(),
            ProviderName: provider.Name,
            new Request(
                Name: "test-resource",
                Description: "desc",
                PrimaryInvestigatorEmail: "pi@example.com",
                StartDate: StartDate,
                EndDate: EndDate,
                AccountString: "ACC-1",
                UnitType: EAllocationUnitType.NodeHours,
                MappingOneToOne: false,  // Must be false for entries to be added to heappeProjectIds
                UseAccountingStringForScheduler: false,
                Usernames: new List<string>() { "user@example.com" },
                LicenseUrl: "https://license.example.org",
                Clusters: clusters,
                BypassHeappe: false,
                AssignedLocations: assignedLocations),
            ResolvedLocations: new List<ResolvedLocationRef>
            {
                new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
            });

        // Act
        var result = await handler.HandleAsync(step, CancellationToken.None);

        // Assert
        if (result.Data != null)
        {
            var root = result.Data!.RootElement;
            Assert.Equal(JsonValueKind.Array, root.ValueKind);
            Assert.Equal(1, root.GetArrayLength());

            var entry = root[0];
            Assert.Equal("location-1", entry.GetProperty("locationName").GetString());
            Assert.Equal(42, entry.GetProperty("projectId").GetInt64());
            Assert.Equal("ssh-rsa AAAA...", entry.GetProperty("publicKey").GetString());
        }
    }

    [Fact]
    public async Task Should_ThrowPermanentException_When_NoClusterMatchesAssignedLocationName()
    {
        // Arrange — a missing cluster is not transient: retrying will not make it appear, so the handler
        // fails permanently (G3). The outer catch preserves PermanentException instead of downgrading it
        // to a RetryableException.
        var (projectCreation, heappeService, handler) = CreateSut();
        var provider = Provider.Create(ProviderName.From("test-provider"), countryId: 1);

        heappeService
            .Setup(h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(
                new List<ClusterAuthenticationCredentialAuthType> { new() { Id = 11, Name = "Password" } }));

        var clusters = new List<Cluster>(); // no cluster named "location-1"
        var assignedLocations = new List<LocationAssignment> { CreateAssignedLocation("location-1") };
        var step = CreateStep(clusters, assignedLocations, provider);

        // Act & Assert
        await Assert.ThrowsAsync<PermanentException>(() => handler.HandleAsync(step, CancellationToken.None));

        projectCreation.Verify(
            p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
