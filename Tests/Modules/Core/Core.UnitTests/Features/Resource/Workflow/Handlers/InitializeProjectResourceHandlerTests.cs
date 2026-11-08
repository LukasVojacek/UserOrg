using Core.Domain.Provider;
using Core.Domain.Primitives;
using Core.Domain.Shared;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Exceptions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Behaviour tests for <see cref="InitializeProjectResourceHandler"/> over mocked collaborators: what it
/// provisions, what it persists, and which failures it raises. Originally written to pin the success/failure
/// paths against the G2 dead-code cleanup; extended by T-055 to state which <see cref="Provider"/> instance the
/// handler is allowed to provision over — the one it resolves from <see cref="IProviderRepository"/> in its own
/// DI scope, never the one carried in the step payload from the endpoint's disposed scope.
/// The end-to-end proof (real EF, real INSERTs) lives in
/// <c>InitializeProjectResourceHandlerPersistenceTests</c>; these tests pin the contract at the seam.
/// </summary>
public sealed class InitializeProjectResourceHandlerTests : IDisposable
{
    // Owns the container CreateSut builds the handler from; disposed with the test class instance (one per test).
    private ServiceProvider? _serviceProvider;

    public void Dispose() => _serviceProvider?.Dispose();

    private static Request CreateRequest() => new(
        Name: "test-resource",
        Description: "desc",
        PrimaryInvestigatorEmail: "pi@example.com",
        StartDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        EndDate: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        AccountString: "ACC-1",
        UnitType: EAllocationUnitType.NodeHours,
        MappingOneToOne: true,
        UseAccountingStringForScheduler: false,
        Usernames: new List<string>() { "user@example.com" },
        LicenseUrl: null,
        BypassHeappe: false,
        Clusters: new List<Cluster> { new(ClusterName: "cluster-1", ScratchPath: "/scratch", ProjectPath: "/project") },
        AssignedLocations: new List<LocationAssignment>
        {
            new(
                LocationName: "location-1",
                AllocationUnitType: EAllocationUnitType.NodeHours,
                AggregationName: null,
                AllocationUnitCount: 100,
                AllocationDescription: "alloc",
                PreferredAuthenticationType: "pwd",
                Specifications: null)
        });

    private static InitializeProjectResourceStep CreateStep(Request? request = null) => new(
        RequestId: Guid.NewGuid(),
        ProviderName: ProviderName.From("test-provider"),
        Data: request ?? CreateRequest(),
        ResolvedLocations: new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        },
        UserId.From(Guid.NewGuid()));

    /// <summary>
    /// The provider the handler is expected to provision over: a DIFFERENT instance from the one in the step
    /// payload, standing in for the aggregate reloaded inside the handler's own DI scope (T-055).
    /// </summary>
    private static Provider ReloadedProvider() => Provider.Create(ProviderName.From("test-provider"), countryId: 1);

    private (Mock<IResourceProvisioningService> Provisioning, Mock<IUnitOfWork> UnitOfWork, Mock<IRepository<ProjectResource, ProjectResourceKey>> Repository, InitializeProjectResourceHandler Handler, Func<(Provider Provider, NewResourceSpec Resource, UserId UserId, IReadOnlyList<LocationAssignmentSpec> Assignments)?> CapturedArgs)
        CreateSut(Result<ProjectResource, Error> provisionResult, Provider? reloadedProvider = null)
    {
        (Provider, NewResourceSpec, UserId, IReadOnlyList<LocationAssignmentSpec>)? captured = null;

        var provisioning = new Mock<IResourceProvisioningService>();
        provisioning
            .Setup(p => p.Provision(It.IsAny<Provider>(), It.IsAny<NewResourceSpec>(), It.IsAny<UserId>(), It.IsAny<IReadOnlyList<LocationAssignmentSpec>>()))
            .Callback<Provider, NewResourceSpec, UserId, IReadOnlyList<LocationAssignmentSpec>>((provider, resource, userId, assignments) => captured = (provider, resource, userId, assignments))
            .Returns(provisionResult);

        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IRepository<ProjectResource, ProjectResourceKey>>();

        // Both load shapes answer with the same instance, so the test does not dictate WHICH IProviderRepository
        // read the handler picks — only that it provisions over what came back from one of them.
        var providerRepository = new Mock<IProviderRepository>();
        providerRepository
            .Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Provider?>(reloadedProvider));
        providerRepository
            .Setup(r => r.FindWithAllocatedResourcesAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Provider?>(reloadedProvider));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(provisioning.Object);
        services.AddSingleton(unitOfWork.Object);
        services.AddSingleton(repository.Object);
        services.AddSingleton(providerRepository.Object);

        _serviceProvider = services.BuildServiceProvider();

        // Built through the container rather than `new`: the handler gains an IProviderRepository parameter in
        // the T-055 fix, and this fixture must keep compiling and asserting across that constructor change.
        var handler = ActivatorUtilities.CreateInstance<InitializeProjectResourceHandler>(_serviceProvider);

        return (provisioning, unitOfWork, repository, handler, () => captured);
    }

    [Fact]
    public async Task Should_PersistProvisionedResourceAndReturnNullData_When_ProvisionSucceeds()
    {
        // Arrange
        var step = CreateStep();
        var reloaded = ReloadedProvider();
        var resource = ProjectResource.Create(
            ResourceName.Create("test-resource"),
            reloaded,
            UserId.From(Guid.NewGuid()),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        var (provisioning, unitOfWork, repository, handler, capturedArgs) = CreateSut(Result<ProjectResource, Error>.Success(resource), reloaded);

        // Act
        var result = await handler.HandleAsync(step, CancellationToken.None);

        // Assert
        Assert.Null(result.Data);
        repository.Verify(r => r.AddAsync(resource, It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        provisioning.Verify(p => p.Provision(It.IsAny<Provider>(), It.IsAny<NewResourceSpec>(), It.IsAny<UserId>(), It.IsAny<IReadOnlyList<LocationAssignmentSpec>>()), Times.Once);

        var captured = capturedArgs();
        Assert.NotNull(captured);

        // T-055: the handler must provision over the aggregate IT loaded, not over an instance that rode in
        // the step payload from the (by then disposed) endpoint scope. The previous revision of this test
        // asserted Assert.Same(step.Provider, …) and so pinned the defect as intended behaviour.
        // T-055-04 removed the payload's Provider member outright, so the companion
        // Assert.NotSame(step.Provider, …) lost its subject; that the aggregate cannot ride along at all is now
        // pinned by CreateResourceStepPayloadContractTests.
        Assert.Same(reloaded, captured.Value.Provider);
        Assert.Equal(ResourceName.Create("test-resource"), captured.Value.Resource.Name);
        Assert.Equal(Email.Create("pi@example.com"), captured.Value.Resource.PrimaryInvestigatorEmail);
        Assert.Single(captured.Value.Assignments);
        Assert.Equal(LocationName.Create("location-1"), captured.Value.Assignments[0].LocationName);
        Assert.Equal(100, captured.Value.Assignments[0].Allocation.AllocationUnitCount);
    }

    [Fact]
    public async Task Should_ThrowRetryableException_When_ProvisionFails()
    {
        // Arrange
        var error = new ErrorBuilder()
            .WithCode("resource.provision_failed")
            .WithDescription("boom")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

        var (_, unitOfWork, repository, handler, _) = CreateSut(Result<ProjectResource, Error>.Failure(error), ReloadedProvider());
        var step = CreateStep();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RetryableException>(() => handler.HandleAsync(step, CancellationToken.None));
        Assert.Equal("FAILED_PROVISIONING", exception.ErrorInfo.ErrorCode);

        repository.Verify(r => r.AddAsync(It.IsAny<ProjectResource>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_NotSwallowAnUnrelatedDbUpdateException_When_TheSaveFails()
    {
        // Guard rail on the T-055-02 fix, not a new behaviour: acceptance 2 asks for the
        // (ProviderId, ResourceName) unique-index violation — and only that — to be translated into an
        // idempotent success. A blanket `catch (DbUpdateException) { return new StepResult(null); }` would
        // satisfy every other test in this suite while silently reporting genuinely failed writes (a lost FK
        // target, a length overflow, a concurrency token mismatch) as completed workflow steps, which is the
        // exact operator-visible lie T-055-02 exists to remove, only pointed the other way.
        //
        // Green today (the exception simply propagates); it must stay green after the fix.
        //
        // The ProjectResource repository mock answers FindAsync with null, so a fix that re-reads after the
        // failed save to decide whether the row is really there also reaches the rethrow: nothing was written.
        var reloaded = ReloadedProvider();
        var resource = ProjectResource.Create(
            ResourceName.Create("test-resource"),
            reloaded,
            UserId.From(Guid.NewGuid()),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        var (_, unitOfWork, _, handler, _) = CreateSut(Result<ProjectResource, Error>.Success(resource), reloaded);

        var failure = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException("The DELETE statement conflicted with the REFERENCE constraint 'FK_Something_Else'."));

        unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(CreateStep(), CancellationToken.None));

        // Not pinned to DbUpdateException itself: wrapping it in a RetryableException/PermanentException is a
        // legitimate implementation choice. What must not happen is the step reporting success.
        Assert.True(
            ReferenceEquals(thrown, failure) || ReferenceEquals(thrown.InnerException, failure),
            $"The unrelated save failure must reach the caller, not be translated to success. Got: {thrown}");
    }

    [Fact]
    public async Task Should_ThrowPermanentException_When_ProviderIsNotFoundInTheHandlersScope()
    {
        // A provider that no longer resolves in the worker's scope is a permanent failure — retrying cannot
        // conjure the row back, and provisioning over the payload's copy would silently INSERT a duplicate
        // provider (T-055). Nothing is provisioned and nothing is saved.
        var resource = ProjectResource.Create(
            ResourceName.Create("test-resource"),
            ReloadedProvider(),
            UserId.From(Guid.NewGuid()),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

        var (provisioning, unitOfWork, repository, handler, _) = CreateSut(Result<ProjectResource, Error>.Success(resource), reloadedProvider: null);
        var step = CreateStep();

        var exception = await Assert.ThrowsAsync<PermanentException>(() => handler.HandleAsync(step, CancellationToken.None));
        Assert.Equal("PROVIDER_NOT_FOUND", exception.ErrorInfo.ErrorCode);

        provisioning.Verify(p => p.Provision(It.IsAny<Provider>(), It.IsAny<NewResourceSpec>(), It.IsAny<UserId>(), It.IsAny<IReadOnlyList<LocationAssignmentSpec>>()), Times.Never);
        repository.Verify(r => r.AddAsync(It.IsAny<ProjectResource>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
