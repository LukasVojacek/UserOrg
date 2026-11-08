using System.Text.Json;

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

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Exceptions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Characterization tests for <see cref="InitializeHeappeHandler.HandleAsync"/>, written before the T-197-07
/// extract-method refactor. They pin the HEAppE project specification the handler builds per assigned location,
/// the usage-type mapping, which failures stay permanent and which the outer catch rewraps as retryable, the SSH key
/// request for non one-to-one mapping, and the resource state update after every location succeeded.
/// </summary>
public sealed class InitializeHeappeHandlerCharacterizationTests
{
    private const string ResourceNameValue = "test-resource";
    private const string LocationValue = "Location-1";
    private const string OtherLocationValue = "location-2";
    private const string HeappeUriWithSlash = "https://heappe.example.org/";
    private const string HeappeIdentifier = "heappe-id-1";
    private const string Username = "user@example.com";
    private const long ProjectId = 42;
    private const int AuthTypeId = 7;

    private static readonly ProviderName TestProvider = ProviderName.From("test-provider");
    private static readonly DateTimeOffset StartDate = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndDate = new(2026, 12, 31, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IProjectCreationService> _projectCreation = new();
    private readonly Mock<IHeappeService> _heappeService = new();
    private readonly Mock<IHeappeConnectionFactory> _connectionFactory = new();
    private readonly Mock<IRepository<ResourceAggregate, ProjectResourceKey>> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<INotificationSender> _notificationSender = new();
    private readonly Mock<IProviderRepository> _providerRepository = new();
    private readonly ResourceAggregate _resource = ResourceAggregate.Create(
        ResourceName.Create(ResourceNameValue), Core.Domain.Provider.Provider.Create(TestProvider, countryId: 1),
        UserId.From(Guid.NewGuid()),
        StartDate, EndDate,
        description: "old",
        primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));

    private readonly List<HeappeProjectSpecificationEntry> _specs = [];
    private readonly List<SecureShellKeyRequest> _keyRequests = [];

    public InitializeHeappeHandlerCharacterizationTests()
    {
        _heappeService
            .Setup(h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ClusterAuthenticationCredentialAuthType>, Error>.Success(
                new List<ClusterAuthenticationCredentialAuthType> { new() { Id = 3, Name = "Other" }, new() { Id = AuthTypeId, Name = "Password" } }));

        _projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .Callback<HeappeProjectSpecificationEntry, CancellationToken>((spec, _) => _specs.Add(spec))
            .ReturnsAsync(() => Result<long, Error>.Success(ProjectId + _specs.Count - 1));

        _projectCreation
            .Setup(p => p.GetServiceHeappeConnectionInfoAsync(
                It.IsAny<UserOrg.Shared.HeappeIdentifier>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(
                new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), new UserOrg.Shared.HeappeIdentifier(HeappeIdentifier))));

        _connectionFactory
            .Setup(f => f.ForPublicAsync(It.IsAny<UserOrg.Shared.HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserOrg.Shared.HeappeIdentifier identifier, CancellationToken _) =>
                Result<PublicHeappeConnectionInfo, Error>.Success(new PublicHeappeConnectionInfo(new Uri("https://heappe.example.org/"), identifier)));

        _heappeService
            .Setup(h => h.GenerateSecureShellKeyAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<SecureShellKeyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BaseHeappeConnectionInfo, SecureShellKeyRequest, CancellationToken>((_, request, _) => _keyRequests.Add(request))
            .ReturnsAsync((BaseHeappeConnectionInfo _, SecureShellKeyRequest request, CancellationToken _) =>
                Result<IList<SecureShellKeyModel>, Error>.Success(
                    new List<SecureShellKeyModel> { new(Username, 0, $"ssh-rsa key-{request.ProjectId}", "pem"), new(Username, 0, "ssh-rsa second", "pem") }));

        _repository
            .Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(_resource));

        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _providerRepository
            .Setup(p => p.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Provider.Create(
                TestProvider,
                countryId: 1,
                supportEmail: Email.Create("support@provider.test")));
    }

    private InitializeHeappeHandler Handler() => new(
        new Mock<ILogger<InitializeHeappeHandler>>().Object,
        _projectCreation.Object,
        _heappeService.Object,
        _connectionFactory.Object,
        _repository.Object,
        _unitOfWork.Object,
        _notificationSender.Object,
        _providerRepository.Object);

    private static LocationAssignment Assignment(
        string locationName = LocationValue,
        EAllocationUnitType unitType = EAllocationUnitType.CoreHours,
        long? unitCount = 100,
        string preferredAuthenticationType = "password",
        IReadOnlyList<SpecificationItem>? specifications = null) => new(
        LocationName: locationName,
        AllocationUnitType: unitType,
        AggregationName: "agg",
        AllocationUnitCount: unitCount,
        AllocationDescription: "alloc",
        PreferredAuthenticationType: preferredAuthenticationType,
        Specifications: specifications ??
        [
            new(ApplicationConstants.HeappeUriKey, HeappeUriWithSlash),
            new(ApplicationConstants.HeappeIdentifierKey, HeappeIdentifier),
        ]);

    private static Cluster ClusterFor(string locationName) => new(locationName, $"/scratch/{locationName}", $"/project/{locationName}");

    private static InitializeHeappeStep Step(
        List<LocationAssignment> assignments,
        bool mappingOneToOne = true,
        List<Cluster>? clusters = null,
        List<ResolvedLocationRef>? resolved = null) => new(
        RequestId: Guid.NewGuid(),
        ProviderName: TestProvider,
        new Request(
            Name: ResourceNameValue,
            Description: "desc",
            PrimaryInvestigatorEmail: "pi@example.com",
            StartDate: StartDate,
            EndDate: EndDate,
            AccountString: "ACC-1",
            UnitType: EAllocationUnitType.NodeHours,
            MappingOneToOne: mappingOneToOne,
            UseAccountingStringForScheduler: true,
            Clusters: clusters ?? [.. assignments.Select(a => ClusterFor(a.LocationName))],
            Usernames: new List<string>() { Username },
            LicenseUrl: null,
            BypassHeappe: false,
            AssignedLocations: assignments),
        ResolvedLocations: resolved ?? [new("location-1", Domain.Shared.ELocationType.HPC, "https://login.location-1", "https://heappe.example.org")]);

    private static Error Failure(string description) =>
        new ErrorBuilder().WithDescription(description).WithSeverity(ErrorSeverity.Error).Build();

    [Fact]
    public async Task Should_SendTheFullProjectSpecification_When_InitializingOneLocation()
    {
        await Handler().HandleAsync(Step([Assignment()]), CancellationToken.None);

        var spec = Assert.Single(_specs);
        Assert.Equal(new Uri("https://heappe.example.org"), spec.HeappeUri);
        Assert.Equal(new UserOrg.Shared.HeappeIdentifier(HeappeIdentifier), spec.HeappeIdentifier);
        Assert.Equal(
            new ProjectSpecificationEntry(
                ResourceNameValue, "ACC-1", "desc", StartDate.DateTime, EndDate.DateTime, EUsageType.CoreHours,
                UseAccountingStringForScheduler: true, "pi@example.com", IsOneToOneMapping: true, PreferredAuthType: AuthTypeId),
            spec.ProjectSpecification);
        Assert.Equal(
            new ClusterSpecificationEntry("https://login.location-1", "/project/Location-1", "/scratch/Location-1", new AllocationSpecificationEntry("agg", 100)),
            spec.ClusterSpecification);
        _heappeService.Verify(
            h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(
                It.Is<BaseHeappeConnectionInfo>(c => c is PublicHeappeConnectionInfo && c.BaseAddress == new Uri("https://heappe.example.org")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(EAllocationUnitType.CoreHours, EUsageType.CoreHours)]
    [InlineData(EAllocationUnitType.NodeHours, EUsageType.NodeHours)]
    [InlineData(EAllocationUnitType.None, EUsageType.Unspecified)]
    public async Task Should_MapTheAssignmentUnitTypeToTheHeappeUsageType(EAllocationUnitType unitType, EUsageType expected)
    {
        await Handler().HandleAsync(Step([Assignment(unitType: unitType)]), CancellationToken.None);

        Assert.Equal(expected, Assert.Single(_specs).ProjectSpecification.UsageType);
    }

    [Fact]
    public async Task Should_SendAnEmptyEndpointAndZeroAllocation_When_NoResolvedLocationMatchesAndCountIsNull()
    {
        var step = Step([Assignment(unitCount: null)], resolved: [new(LocationValue, Domain.Shared.ELocationType.HPC, string.Empty, null)]);

        await Handler().HandleAsync(step, CancellationToken.None);

        var cluster = Assert.Single(_specs).ClusterSpecification;
        Assert.Equal(string.Empty, cluster.LoginNodeName);
        Assert.Equal(0, cluster.AllocationSpecification.AllocationAmount);
    }

    [Fact]
    public async Task Should_ThrowPermanentAuthTypeFailureAndNotInitialize_When_PreferredAuthTypeIsUnknown()
    {
        var step = Step([Assignment(preferredAuthenticationType: "Kerberos")]);

        var ex = await Assert.ThrowsAsync<PermanentException>(() => Handler().HandleAsync(step, CancellationToken.None));

        Assert.Equal("HEAPPE_FAILED_AUTH_TYPE", ex.ErrorInfo.ErrorCode);
        Assert.Empty(_specs);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_RewrapTheInitializationFailureAsRetryableAndNotSave_When_HeappeRejectsTheProject()
    {
        _projectCreation
            .Setup(p => p.InitializeHEAppEProjectWithClustersAsync(It.IsAny<HeappeProjectSpecificationEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<long, Error>.Failure(Failure("quota exceeded")));

        var ex = await Assert.ThrowsAsync<RetryableException>(() => Handler().HandleAsync(Step([Assignment()]), CancellationToken.None));

        Assert.Equal("HEAPPE_FAILED_INITIALIZATION", ex.ErrorInfo.ErrorCode);
        Assert.StartsWith($"Failed to initialize HEAppE project resource {ResourceNameValue}", ex.Message);
        var inner = Assert.IsType<RetryableException>(ex.InnerException);
        Assert.Equal("HEAPPE_FAILED_INITIALIZATION", inner.ErrorInfo.ErrorCode);
        Assert.Contains("quota exceeded", inner.Message, StringComparison.Ordinal);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_RewrapTheKeyGenerationFailureAsRetryableInitializationFailure_When_MappingIsNotOneToOne()
    {
        _heappeService
            .Setup(h => h.GenerateSecureShellKeyAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<SecureShellKeyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<SecureShellKeyModel>, Error>.Failure(Failure("no key")));

        var ex = await Assert.ThrowsAsync<RetryableException>(
            () => Handler().HandleAsync(Step([Assignment()], mappingOneToOne: false), CancellationToken.None));

        Assert.Equal("HEAPPE_FAILED_INITIALIZATION", ex.ErrorInfo.ErrorCode);
        var inner = Assert.IsType<RetryableException>(ex.InnerException);
        Assert.Equal("HEAPPE_FAILED_KEY_GENERATION", inner.ErrorInfo.ErrorCode);
        Assert.Contains("no key", inner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_RewrapAnUnexpectedServiceExceptionAsRetryable()
    {
        var boom = new HttpRequestException("connection refused");
        _heappeService
            .Setup(h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        var ex = await Assert.ThrowsAsync<RetryableException>(() => Handler().HandleAsync(Step([Assignment()]), CancellationToken.None));

        Assert.Equal("HEAPPE_FAILED_INITIALIZATION", ex.ErrorInfo.ErrorCode);
        Assert.Same(boom, ex.InnerException);
    }

    [Fact]
    public async Task Should_FailPermanentlyWithoutCallingHeappe_When_TheIdentifierSpecificationIsMissing()
    {
        var step = Step([Assignment(specifications: [new(ApplicationConstants.HeappeUriKey, HeappeUriWithSlash)])]);

        var ex = await Assert.ThrowsAsync<PermanentException>(() => Handler().HandleAsync(step, CancellationToken.None));

        Assert.Equal("HEAPPE_IDENTIFIER_INVALID", ex.ErrorInfo.ErrorCode);
        _heappeService.Verify(
            h => h.GetClusterAuthenticationCredentialsAuthTypesAsync(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_FailPermanently_When_SpecificationsAreNull()
    {
        var step = Step([Assignment() with { Specifications = null }]);

        var ex = await Assert.ThrowsAsync<PermanentException>(() => Handler().HandleAsync(step, CancellationToken.None));

        Assert.Equal("HEAPPE_IDENTIFIER_INVALID", ex.ErrorInfo.ErrorCode);
    }

    [Fact]
    public async Task Should_InitializeWithoutAUrl_When_TheHeappeUriSpecificationIsMissing()
    {
        var step = Step([Assignment(specifications: [new(ApplicationConstants.HeappeIdentifierKey, HeappeIdentifier)])]);

        await Handler().HandleAsync(step, CancellationToken.None);

        Assert.Null(Assert.Single(_specs).HeappeUri);
    }

    [Fact]
    public async Task Should_InitializeWithoutAUrl_When_TheHeappeUriSpecificationIsNotAnAbsoluteUri()
    {
        var step = Step([Assignment(specifications:
        [
            new(ApplicationConstants.HeappeUriKey, "not a uri"),
            new(ApplicationConstants.HeappeIdentifierKey, HeappeIdentifier),
        ])]);

        await Handler().HandleAsync(step, CancellationToken.None);

        Assert.Null(Assert.Single(_specs).HeappeUri);
    }

    [Fact]
    public async Task Should_RewrapAnUnresolvableInstanceAsRetryable_When_TheConnectionFactoryFails()
    {
        _connectionFactory
            .Setup(f => f.ForPublicAsync(It.IsAny<UserOrg.Shared.HeappeIdentifier>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PublicHeappeConnectionInfo, Error>.Failure(Failure("nexus down")));

        var ex = await Assert.ThrowsAsync<RetryableException>(() => Handler().HandleAsync(Step([Assignment()]), CancellationToken.None));

        Assert.Equal("HEAPPE_FAILED_INITIALIZATION", ex.ErrorInfo.ErrorCode);
        var inner = Assert.IsType<RetryableException>(ex.InnerException);
        Assert.Equal("HEAPPE_FAILED_SERVICE_CONNECTION", inner.ErrorInfo.ErrorCode);
    }

    [Fact]
    public async Task Should_SkipKeyGenerationReturnAnEmptyArrayAndAdvanceTheState_When_MappingIsOneToOne()
    {
        var result = await Handler().HandleAsync(Step([Assignment()], mappingOneToOne: true), CancellationToken.None);

        Assert.Equal(JsonValueKind.Array, result.Data!.RootElement.ValueKind);
        Assert.Equal(0, result.Data.RootElement.GetArrayLength());
        Assert.Empty(_keyRequests);
        Assert.Equal(EProjectResourceState.InitializeHeappeStep, _resource.State);
        _repository.Verify(
            r => r.FindAsync(new ProjectResourceKey(TestProvider, ResourceName.Create(ResourceNameValue)), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_RequestOneKeyPerLocationAndReturnEveryLocationInOrder_When_MappingIsNotOneToOne()
    {
        var step = Step(
            [Assignment(), Assignment(OtherLocationValue)],
            mappingOneToOne: false,
            resolved: [new("location-1", Domain.Shared.ELocationType.HPC, "https://login.location-1", null), new(OtherLocationValue, Domain.Shared.ELocationType.HPC, "https://login.location-2", null)]);

        var result = await Handler().HandleAsync(step, CancellationToken.None);

        Assert.Equal(["https://login.location-1", "https://login.location-2"], _specs.Select(s => s.ClusterSpecification.LoginNodeName));
        Assert.Equal([ProjectId, ProjectId + 1], _keyRequests.Select(k => k.ProjectId));
        Assert.All(_keyRequests, k => Assert.Equal(new Credentials(Username, null), Assert.Single(k.Credentials)));

        if (result.Data != null)
        {
            var entries = result.Data!.RootElement;
            Assert.Equal(2, entries.GetArrayLength());
            Assert.Equal(LocationValue, entries[0].GetProperty("locationName").GetString());
            Assert.Equal(ProjectId, entries[0].GetProperty("projectId").GetInt64());
            Assert.Equal($"ssh-rsa key-{ProjectId}", entries[0].GetProperty("publicKey").GetString());
            Assert.Equal(OtherLocationValue, entries[1].GetProperty("locationName").GetString());
            Assert.Equal(ProjectId + 1, entries[1].GetProperty("projectId").GetInt64());
        }
        Assert.Equal(EProjectResourceState.WaitingForCreationAccounts, _resource.State);
    }

    [Fact]
    public async Task Should_EmitIndentedJsonText_When_MappingIsNotOneToOne()
    {
        var step = Step(
            [Assignment(), Assignment(OtherLocationValue)],
            mappingOneToOne: false,
            resolved: [new("location-1", Domain.Shared.ELocationType.HPC, "https://login.location-1", null), new(OtherLocationValue, Domain.Shared.ELocationType.HPC, "https://login.location-2", null)]);

        var result = await Handler().HandleAsync(step, CancellationToken.None);

        var expected = $$"""
            [
              {
                "locationName": "{{LocationValue}}",
                "projectId": {{ProjectId}},
                "publicKey": "ssh-rsa key-{{ProjectId}}"
              },
              {
                "locationName": "{{OtherLocationValue}}",
                "projectId": {{ProjectId + 1}},
                "publicKey": "ssh-rsa key-{{ProjectId + 1}}"
              }
            ]
            """.ReplaceLineEndings(Environment.NewLine);

        if (result.Data != null)
            Assert.Equal(expected, result.Data!.RootElement.GetRawText());
    }

    [Fact]
    public async Task Should_ThrowPermanentResourceNotFoundAfterInitializingHeappe_When_TheResourceIsMissing()
    {
        _repository
            .Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ResourceAggregate?>(null));

        var ex = await Assert.ThrowsAsync<PermanentException>(() => Handler().HandleAsync(Step([Assignment()]), CancellationToken.None));

        Assert.Equal("RESOURCE_NOT_FOUND", ex.ErrorInfo.ErrorCode);
        Assert.Single(_specs);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
