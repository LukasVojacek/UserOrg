using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Domain.Entities;

using Waldur.Adapter.Tests.Fakes;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Guards the six sites in the Waldur context extensions that carry an incoming source entity's
/// <c>Id</c> onto the entity being inserted: a source that already carries an id keeps it, and the
/// site never mints one of its own. The shared <c>ApplyCodeGeneratedGuidKeys()</c> convention is the
/// only other id source — it stamps the key at <c>Add</c> time when the source leaves it unset,
/// which is what the Waldur exporters do (ADR-0061, EntityIdGenerationConventionTests).
/// </summary>
public sealed class WaldurConditionalIdCollapseTests
{
    [Fact(DisplayName = "UpsertProjectResourceAsync persists the source ProjectResource's Id unchanged")]
    public async Task UpsertProjectResourceAsync_Should_PersistSourceId_When_ResourceIsNew()
    {
        using var context = new RecordingUserOrgDbContext();
        var cancellationToken = TestContext.Current.CancellationToken;

        var resource = new ProjectResource { Id = Guid.CreateVersion7(), Name = "backend-0001", ServiceName = "waldur" };

        var persisted = await context.UpsertProjectResourceAsync(resource, cancellationToken);

        Assert.Equal(resource.Id, persisted.Id);
    }

    [Fact(DisplayName = "UpsertAssignedLocationAsync persists the source AssignedLocation's and its AssignedLocationValues' Ids unchanged")]
    public async Task UpsertAssignedLocationAsync_Should_PersistSourceIds_When_AssignedLocationIsNew()
    {
        using var context = new RecordingUserOrgDbContext();
        var cancellationToken = TestContext.Current.CancellationToken;

        var dbResource = new ProjectResource
        {
            Name = "backend-0001",
            ServiceName = "waldur",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        context.Add(dbResource);
        var dbAllocationUnitType = new AllocationUnitType { Id = 1, Name = "node-hours" };
        context.Add(dbAllocationUnitType);
        await context.SaveChangesAsync(cancellationToken);

        var assignedLocationValue = new AssignedLocationValue { Id = Guid.CreateVersion7(), Name = "cores", Value = "16" };
        var assignedLocation = new AssignedLocation
        {
            Id = Guid.CreateVersion7(),
            ExternalId = "external-1",
            ServiceName = "waldur",
            Mode = EOperationMode.ReadOnly,
            AssignedLocationsValues = [assignedLocationValue]
        };

        var persisted = await context.UpsertAssignedLocationAsync(assignedLocation, dbResource, dbAllocationUnitType, cancellationToken);

        Assert.Equal(assignedLocation.Id, persisted.Id);
        Assert.Equal(assignedLocationValue.Id, Assert.Single(persisted.AssignedLocationsValues).Id);
    }

    [Fact(DisplayName = "UpsertProviderAsync persists the source Provider's Id unchanged")]
    public async Task UpsertProviderAsync_Should_PersistSourceId_When_ProviderIsNew()
    {
        using var context = new RecordingUserOrgDbContext();
        var cancellationToken = TestContext.Current.CancellationToken;

        var dbCountry = new Country { Id = 1, Name = "CZ" };
        context.Add(dbCountry);
        var dbUser = new User { IdentityProviderUserId = "waldur", Email = "waldur@example.com", UserName = "waldur" };
        context.Add(dbUser);
        await context.SaveChangesAsync(cancellationToken);

        var provider = new Provider { Id = Guid.CreateVersion7(), Name = "HPC Provider", ServiceName = "waldur" };

        var persisted = await context.UpsertProviderAsync(NullLogger.Instance, "waldur", provider, dbCountry, dbUser, cancellationToken);

        Assert.Equal(provider.Id, persisted.Id);
    }

    [Fact(DisplayName = "UpsertLocationAsync persists the source Location's Id unchanged")]
    public async Task UpsertLocationAsync_Should_PersistSourceId_When_LocationIsNew()
    {
        using var context = new RecordingUserOrgDbContext();
        var cancellationToken = TestContext.Current.CancellationToken;

        var dbProvider = new Provider { Name = "HPC Provider", ServiceName = "waldur" };
        context.Add(dbProvider);
        var dbLocationType = new LocationType { Id = 1, Name = "HPC" };
        context.Add(dbLocationType);
        await context.SaveChangesAsync(cancellationToken);

        var location = new Location { Id = Guid.CreateVersion7(), Name = "hpc-1", Endpoint = "hpc-1.example.com", ServiceName = "waldur" };

        await context.UpsertLocationAsync(location, dbProvider, dbLocationType, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var persisted = await context.LocationsQuery.SingleAsync(cancellationToken);
        Assert.Equal(location.Id, persisted.Id);
    }
}
