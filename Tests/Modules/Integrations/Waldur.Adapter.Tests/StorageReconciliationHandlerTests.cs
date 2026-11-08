using System.Collections.Immutable;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Sync;
using Userorg.Adapter.Waldur.Sync.Handlers;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Behaviour-matrix tests for the B10 <see cref="StorageReconciliationHandler"/> (ADR-0061): the
/// EFP-Core provider + shared/per-provider location lifecycle and the resource→storage association
/// sweep. Backed by the InMemory <see cref="FakeUserOrgDbContext"/>.
/// </summary>
public sealed class StorageReconciliationHandlerTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private const string EfpCore = "EFP-Core";
    private const int HpcTypeId = (int)ELocationType.HPC;
    private const int StorageTypeId = (int)ELocationType.Storage;
    private const int IrodsTypeId = (int)ELocationType.iRODS;

    private static readonly Guid ProviderExternalId = Guid.NewGuid();

    private static StorageReconciliationHandler Handler(IUserOrgDbContext context) =>
        new(context, NullLogger<StorageReconciliationHandler>.Instance);

    private static StorageConfigurationChanged Event(StorageConfigPayload config) =>
        new(config) { Instance = Instance, ServiceName = ServiceName };

    private static StorageConfigPayload Config(
        bool useSharedForAll = false,
        StorageInstancePayload? sharedIrods = null,
        IImmutableList<StorageInstancePayload>? irods = null,
        IImmutableList<StorageInstancePayload>? staging = null) =>
        new(EfpCore, useSharedForAll, sharedIrods, irods ?? [], staging ?? []);

    private static StorageInstancePayload SharedIrods(int? amount = 50) =>
        new(null, "shared-irods", "irods://shared", "iRODS", amount);

    private static StorageInstancePayload ProviderIrods(int? amount = 100) =>
        new(ProviderExternalId.ToString("N"), "prov-irods", "irods://prov", "iRODS", amount);

    private static StorageInstancePayload ProviderStaging() =>
        new(ProviderExternalId.ToString("N"), "prov-staging", "file://staging", "Storage", null);

    private static FakeUserOrgDbContext SeededContext(bool seedServiceAccount = true)
    {
        var context = new FakeUserOrgDbContext();
        context.Seed<LocationType>(
        [
            new LocationType { Id = HpcTypeId, Name = "HPC" },
            new LocationType { Id = StorageTypeId, Name = "Storage" },
            new LocationType { Id = IrodsTypeId, Name = "iRODS" },
        ]);
        context.Seed<AllocationUnitType>(
        [
            new AllocationUnitType { Id = 1, Name = "unspecified" },
            new AllocationUnitType { Id = 4, Name = "GB" },
        ]);

        if (seedServiceAccount)
        {
            context.Seed<User>([new User
            {
                Id = Guid.CreateVersion7(),
                IdentityProviderUserId = ServiceName,
                Email = "svc@waldur",
                UserName = ServiceName,
                CreatedAt = DateTime.UtcNow,
            }]);
        }

        return context;
    }

    /// <summary>Seeds an instance provider (HPC) + an HPC-assigned resource, returns the provider id.</summary>
    private static (Guid ProviderId, Guid ResourceId) SeedProviderResource(FakeUserOrgDbContext context)
    {
        var providerId = Guid.CreateVersion7();
        context.Seed<Provider>([new Provider
        {
            Id = providerId,
            Name = "Acme HPC",
            Country = new Country { Name = "CZ" },
            ExternalId = ProviderExternalId.ToString(),
            ServiceName = ServiceName,
            Mode = EOperationMode.ReadOnly,
            CreatedAt = DateTime.UtcNow,
        }]);

        var hpcLocationId = Guid.CreateVersion7();
        context.Seed<Location>([new Location
        {
            Id = hpcLocationId,
            Name = "karolina",
            Endpoint = "karolina.it4i.cz",
            ProviderId = providerId,
            LocationTypeId = HpcTypeId,
            ServiceName = ServiceName,
            Mode = EOperationMode.ReadOnly,
            CreatedAt = DateTime.UtcNow,
        }]);

        var resourceId = Guid.CreateVersion7();
        context.Seed<ProjectResource>([new ProjectResource
        {
            Id = resourceId,
            Name = "res-1",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30),
            ServiceName = ServiceName,
            Mode = EOperationMode.ReadOnly,
            CreatedAt = DateTime.UtcNow,
        }]);

        context.Seed<AssignedLocation>([new AssignedLocation
        {
            Id = Guid.CreateVersion7(),
            ProjectResourceId = resourceId,
            LocationId = hpcLocationId,
            AllocationUnitTypeId = 1,
            ServiceName = ServiceName,
            Mode = EOperationMode.ReadOnly,
            CreatedAt = DateTime.UtcNow,
        }]);

        return (providerId, resourceId);
    }

    [Fact(DisplayName = "Creates the EFP-Core provider (active) + shared iRODS when configured and absent")]
    public async Task Should_CreateEfpCoreProviderAndSharedIrods_When_Absent()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Event(Config(sharedIrods: SharedIrods())), CancellationToken.None);

        var provider = await context.ProvidersQuery.SingleAsync(p => p.ServiceName == EfpCore);
        Assert.False(provider.IsDeleted);
        var location = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.LocationTypeId == IrodsTypeId);
        Assert.Equal("shared-irods", location.Name);
        Assert.Equal(provider.Id, location.ProviderId);
    }

    [Fact(DisplayName = "Creates the EFP-Core provider on the existing FI country instead of a second FI row")]
    public async Task Should_ReuseExistingCountry_When_CreatingEfpCoreProvider()
    {
        using var context = SeededContext();
        context.Seed<Country>([new Country { Id = 127, Name = "FI" }]);

        await Handler(context).HandleAsync(Event(Config(sharedIrods: SharedIrods())), CancellationToken.None);

        var provider = await context.ProvidersQuery.Include(p => p.Country).SingleAsync(p => p.ServiceName == EfpCore);
        Assert.Equal(127, provider.Country!.Id);
        Assert.Equal(1, await context.CountriesQuery.CountAsync(c => c.Name == "FI"));
    }

    [Fact(DisplayName = "Creates the EFP-Core provider soft-deleted when no shared iRODS is configured")]
    public async Task Should_CreateEfpCoreProviderSoftDeleted_When_NoSharedIrods()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Event(Config()), CancellationToken.None);

        var provider = await context.ProvidersQuery.SingleAsync(p => p.ServiceName == EfpCore);
        Assert.True(provider.IsDeleted);
    }

    [Fact(DisplayName = "Undeletes the EFP-Core provider + shared iRODS when previously soft-deleted")]
    public async Task Should_UndeleteEfpCoreProviderAndSharedIrods_When_SoftDeleted()
    {
        using var context = SeededContext();
        var providerId = Guid.CreateVersion7();
        context.Seed<Provider>([new Provider
        {
            Id = providerId, Name = EfpCore, Country = new Country { Name = "FI" }, ServiceName = EfpCore,
            IsDeleted = true, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);
        context.Seed<Location>([new Location
        {
            Id = Guid.CreateVersion7(), Name = "shared-irods", Endpoint = "irods://shared", ProviderId = providerId,
            LocationTypeId = IrodsTypeId, ServiceName = EfpCore, IsDeleted = true, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);

        await Handler(context).HandleAsync(Event(Config(sharedIrods: SharedIrods())), CancellationToken.None);

        var provider = await context.ProvidersQuery.SingleAsync(p => p.ServiceName == EfpCore);
        Assert.False(provider.IsDeleted);
        var location = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.LocationTypeId == IrodsTypeId);
        Assert.False(location.IsDeleted);
    }

    [Fact(DisplayName = "Deactivates the EFP-Core provider + its locations when shared iRODS leaves config")]
    public async Task Should_DeactivateEfpCoreProvider_When_SharedIrodsRemoved()
    {
        using var context = SeededContext();
        var providerId = Guid.CreateVersion7();
        context.Seed<Provider>([new Provider
        {
            Id = providerId, Name = EfpCore, Country = new Country { Name = "FI" }, ServiceName = EfpCore,
            IsDeleted = false, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);
        context.Seed<Location>([new Location
        {
            Id = Guid.CreateVersion7(), Name = "shared-irods", Endpoint = "irods://shared", ProviderId = providerId,
            LocationTypeId = IrodsTypeId, ServiceName = EfpCore, IsDeleted = false, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);

        await Handler(context).HandleAsync(Event(Config()), CancellationToken.None);

        var provider = await context.ProvidersQuery.SingleAsync(p => p.ServiceName == EfpCore);
        Assert.True(provider.IsDeleted);
        var location = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.LocationTypeId == IrodsTypeId);
        Assert.True(location.IsDeleted);
    }

    [Fact(DisplayName = "Ensures per-provider iRODS + staging locations from config")]
    public async Task Should_EnsurePerProviderLocations_When_Configured()
    {
        using var context = SeededContext();
        var (providerId, _) = SeedProviderResource(context);

        await Handler(context).HandleAsync(
            Event(Config(irods: [ProviderIrods()], staging: [ProviderStaging()])), CancellationToken.None);

        var irods = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.ProviderId == providerId && l.LocationTypeId == IrodsTypeId);
        Assert.Equal("prov-irods", irods.Name);
        var staging = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.ProviderId == providerId && l.LocationTypeId == StorageTypeId);
        Assert.Equal("prov-staging", staging.Name);
    }

    [Fact(DisplayName = "Deactivates a per-provider iRODS location when its mapping leaves config")]
    public async Task Should_DeactivatePerProviderIrods_When_ConfigRemoved()
    {
        using var context = SeededContext();
        var (providerId, _) = SeedProviderResource(context);
        context.Seed<Location>([new Location
        {
            Id = Guid.CreateVersion7(), Name = "prov-irods", Endpoint = "irods://prov", ProviderId = providerId,
            LocationTypeId = IrodsTypeId, ServiceName = EfpCore, IsDeleted = false, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);

        // Config no longer carries the provider iRODS mapping.
        await Handler(context).HandleAsync(Event(Config()), CancellationToken.None);

        var irods = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.ProviderId == providerId && l.LocationTypeId == IrodsTypeId);
        Assert.True(irods.IsDeleted);
    }

    [Fact(DisplayName = "Attaches the shared iRODS to the resource when UseSharedIrodsForAllEntities")]
    public async Task Should_AttachSharedIrods_When_UseSharedForAll()
    {
        using var context = SeededContext();
        var (_, resourceId) = SeedProviderResource(context);

        await Handler(context).HandleAsync(
            Event(Config(useSharedForAll: true, sharedIrods: SharedIrods(amount: 50))), CancellationToken.None);

        var sharedLocationId = (await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.Name == "shared-irods")).Id;
        var assigned = await context.AssignedLocationsQuery
            .SingleAsync(a => a.ProjectResourceId == resourceId && a.LocationId == sharedLocationId && a.ServiceName == EfpCore);
        Assert.False(assigned.IsDeleted);
        Assert.Equal(50, assigned.AllocationUnitCount);
    }

    [Fact(DisplayName = "Attaches per-provider iRODS + staging with the configured allocation amount")]
    public async Task Should_AttachProviderIrodsAndStaging_When_Configured()
    {
        using var context = SeededContext();
        var (providerId, resourceId) = SeedProviderResource(context);

        await Handler(context).HandleAsync(
            Event(Config(irods: [ProviderIrods(amount: 100)], staging: [ProviderStaging()])), CancellationToken.None);

        var irodsLoc = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.ProviderId == providerId && l.LocationTypeId == IrodsTypeId);
        var irodsAssigned = await context.AssignedLocationsQuery.SingleAsync(a => a.ProjectResourceId == resourceId && a.LocationId == irodsLoc.Id);
        Assert.Equal(100, irodsAssigned.AllocationUnitCount);

        var stagingLoc = await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.ProviderId == providerId && l.LocationTypeId == StorageTypeId);
        Assert.True(await context.AssignedLocationsQuery.AnyAsync(a => a.ProjectResourceId == resourceId && a.LocationId == stagingLoc.Id && !a.IsDeleted));
    }

    [Fact(DisplayName = "Does NOT attach the shared iRODS when the provider has its own iRODS mapping and sharing is not forced")]
    public async Task Should_NotAttachSharedIrods_When_ProviderHasOwnIrods()
    {
        using var context = SeededContext();
        var (_, resourceId) = SeedProviderResource(context);

        await Handler(context).HandleAsync(
            Event(Config(useSharedForAll: false, sharedIrods: SharedIrods(), irods: [ProviderIrods()])), CancellationToken.None);

        var sharedLocationId = (await context.LocationsQuery.SingleAsync(l => l.ServiceName == EfpCore && l.Name == "shared-irods")).Id;
        Assert.False(await context.AssignedLocationsQuery.AnyAsync(a => a.ProjectResourceId == resourceId && a.LocationId == sharedLocationId));
    }

    [Fact(DisplayName = "Replaying an unchanged config produces no duplicate assigned locations (idempotent)")]
    public async Task Should_BeIdempotent_When_Replayed()
    {
        using var context = SeededContext();
        var (_, resourceId) = SeedProviderResource(context);
        var config = Config(useSharedForAll: true, sharedIrods: SharedIrods(), irods: [ProviderIrods()], staging: [ProviderStaging()]);

        await Handler(context).HandleAsync(Event(config), CancellationToken.None);
        await Handler(context).HandleAsync(Event(config), CancellationToken.None);

        var assigned = await context.AssignedLocationsQuery
            .Where(a => a.ProjectResourceId == resourceId && a.ServiceName == EfpCore)
            .ToListAsync();
        // shared iRODS + provider iRODS + staging, each exactly once.
        Assert.Equal(3, assigned.Count);
        Assert.Single(await context.ProvidersQuery.Where(p => p.ServiceName == EfpCore).ToListAsync());
    }

    [Fact(DisplayName = "Deactivates a resource's staging association when the staging mapping leaves config")]
    public async Task Should_DeactivateStagingAssociation_When_ConfigRemoved()
    {
        using var context = SeededContext();
        var (providerId, resourceId) = SeedProviderResource(context);
        var stagingLocationId = Guid.CreateVersion7();
        context.Seed<Location>([new Location
        {
            Id = stagingLocationId, Name = "prov-staging", Endpoint = "file://staging", ProviderId = providerId,
            LocationTypeId = StorageTypeId, ServiceName = EfpCore, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);
        context.Seed<AssignedLocation>([new AssignedLocation
        {
            Id = Guid.CreateVersion7(), ProjectResourceId = resourceId, LocationId = stagingLocationId, AllocationUnitTypeId = 1,
            ServiceName = EfpCore, IsDeleted = false, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);

        await Handler(context).HandleAsync(Event(Config()), CancellationToken.None);

        var assigned = await context.AssignedLocationsQuery.SingleAsync(a => a.LocationId == stagingLocationId);
        Assert.True(assigned.IsDeleted);
    }

    [Fact(DisplayName = "Never mutates a non-EFP-Core Location or assigned location under the same provider (scope)")]
    public async Task Should_NotTouchForeignServiceLocationOrAssignedLocation()
    {
        using var context = SeededContext();
        var (providerId, resourceId) = SeedProviderResource(context);
        var foreignLocationId = Guid.CreateVersion7();
        context.Seed<Location>([new Location
        {
            Id = foreignLocationId, Name = "foreign-irods", Endpoint = "irods://foreign", ProviderId = providerId,
            LocationTypeId = IrodsTypeId, ServiceName = "OTHER-SERVICE", IsDeleted = false, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);
        var foreignAssignedId = Guid.CreateVersion7();
        context.Seed<AssignedLocation>([new AssignedLocation
        {
            Id = foreignAssignedId, ProjectResourceId = resourceId, LocationId = foreignLocationId, AllocationUnitTypeId = 1,
            ServiceName = "OTHER-SERVICE", IsDeleted = false, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow,
        }]);

        // Config carries no iRODS mapping for this provider — the reconcile would deactivate the
        // provider's EFP-Core iRODS Location + association, but the foreign-service rows sharing the
        // same provider must be left untouched (review MEDIUM: Location-level queries are scoped).
        await Handler(context).HandleAsync(Event(Config()), CancellationToken.None);

        var foreignLocation = await context.LocationsQuery.SingleAsync(l => l.Id == foreignLocationId);
        Assert.False(foreignLocation.IsDeleted);
        Assert.Equal("OTHER-SERVICE", foreignLocation.ServiceName);

        var foreignAssigned = await context.AssignedLocationsQuery.SingleAsync(a => a.Id == foreignAssignedId);
        Assert.False(foreignAssigned.IsDeleted);
        Assert.Equal("OTHER-SERVICE", foreignAssigned.ServiceName);
    }

    [Fact(DisplayName = "Returns failure (not acked) when the service account is not bootstrapped")]
    public async Task Should_ReturnFailure_When_ServiceAccountMissing()
    {
        using var context = SeededContext(seedServiceAccount: false);

        var result = await Handler(context).HandleAsync(Event(Config(sharedIrods: SharedIrods())), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await context.ProvidersQuery.Where(p => p.ServiceName == EfpCore).ToListAsync());
    }

    [Fact(DisplayName = "Handler is registered via the B06 seam for StorageConfigurationChanged")]
    public void Should_RegisterHandler_For_StorageConfigurationChanged()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddLogging();
        services.AddWaldurEventConsumers();
        services.AddWaldurStorageReconciliationHandler();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<StorageConfigurationChanged>>());
    }
}
