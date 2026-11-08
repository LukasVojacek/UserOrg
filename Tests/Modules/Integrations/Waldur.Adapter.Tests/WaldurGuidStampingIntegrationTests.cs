using Core.UnitTests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Executed proof (closes B05 criterion 12b) that the twin-creation sites in
/// <see cref="WaldurUserContextExtensions"/>, <see cref="WaldurProviderContextExtensions"/> and
/// <see cref="WaldurProjectResourceContextExtensions"/> get their <c>Id</c> from the shared
/// <c>ApplyCodeGeneratedGuidKeys()</c> convention, not from the source entity they copy. Every
/// source here leaves <c>Id</c> at its default (<see cref="Guid.Empty"/>), matching what the Waldur
/// exporters actually pass in production post-B03 (<see cref="WaldurConditionalIdCollapseTests"/>
/// only covers id-carrying sources, on EF Core's InMemory provider). This suite runs the same
/// upserts against a real SQL Server <see cref="DatabaseContext"/> instead: InMemory mints its own
/// non-empty key once no store default is configured, so a plain non-empty-Id assertion there would
/// pass regardless of whether the convention ran — the version nibble is what actually
/// discriminates, and every test below was proven to fail for that exact reason (stamped version 4,
/// EF's own default <see cref="Guid.NewGuid"/>-based fallback) when
/// <c>ApplyCodeGeneratedGuidKeys()</c> was temporarily removed and this same suite re-run against
/// this same real database. Every fixture row is deleted in a <c>finally</c> (same idiom as
/// <c>OutboxTransactionalIntegrationTests</c>): every FK in this schema is <c>NoAction</c>, so a
/// leaked row can collide with a later run's existing-row lookup and produce a false pass (see
/// <c>UpsertProviderAsync</c>, whose lookup matches on <c>ExternalId + ServiceName</c>).
/// </summary>
/// <remarks>
/// <b>Opt-in, not CI coverage.</b> These 8 tests gate on <c>USERORG_TEST_DB</c>
/// (see <see cref="Core.UnitTests.IntegrationDb"/>); with it unset they SKIP, not fail. No project
/// CI/CD variable of that name is set and it does not appear in the CI YAML (group-level variables
/// were not checked). A regression here still ships green through a normal <c>dotnet build &amp;&amp;
/// dotnet test</c> — this suite catches it only if, and only when, someone runs it by hand. To run
/// it, point <c>USERORG_TEST_DB</c> at a real SQL Server instance migrated from zero by the
/// <c>DatabaseMigrations</c> tool, then:
/// <code>
/// # bash / Git Bash
/// export USERORG_TEST_DB="Server=&lt;host&gt;,&lt;port&gt;;User Id=sa;Password=***;Database=UserOrgDBContext;TrustServerCertificate=True"
///
/// # PowerShell
/// $env:USERORG_TEST_DB = "Server=&lt;host&gt;,&lt;port&gt;;User Id=sa;Password=***;Database=UserOrgDBContext;TrustServerCertificate=True"
///
/// ./artifacts/bin/Waldur.Adapter.Tests/debug/Waldur.Adapter.Tests.exe --filter-class "*WaldurGuidStampingIntegrationTests*"
/// </code>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class WaldurGuidStampingIntegrationTests
{
    [Fact(DisplayName = "UpsertProviderAsync stamps a UUID v7 Id when the source Provider's Id is unset")]
    public async Task UpsertProviderAsync_Should_StampUuidV7Id_When_SourceProviderHasUnsetId()
    {
        IntegrationDb.SkipIfUnavailable();
        using var context = NewContext();
        var ct = TestContext.Current.CancellationToken;
        var token = Guid.NewGuid().ToString("N");

        var dbCountry = new Country { Name = $"b06-country-{token}" };
        var dbUser = new User { IdentityProviderUserId = $"b06-idp-{token}", Email = $"b06-{token}@example.com", UserName = $"b06-user-{token}" };
        context.AddRange(dbCountry, dbUser);
        await context.SaveChangesAsync(ct);

        var provider = new Provider { Name = $"b06-provider-{token}", ServiceName = "waldur", ExternalId = $"b06-external-{token}" };
        Assert.Equal(Guid.Empty, provider.Id);

        try
        {
            var persisted = await context.UpsertProviderAsync(NullLogger.Instance, "waldur", provider, dbCountry, dbUser, ct);
            await context.SaveChangesAsync(ct);

            AssertStampedUuidV7(persisted.Id);
        }
        finally
        {
            // dependents (User.AssignedProviderId, Provider.CountryId) before their principals.
            await context.UsersQuery.Where(u => u.UserName == dbUser.UserName).ExecuteDeleteAsync(ct);
            await context.ProvidersQuery.Where(p => p.ExternalId == provider.ExternalId).ExecuteDeleteAsync(ct);
            await context.CountriesQuery.Where(c => c.Name == dbCountry.Name).ExecuteDeleteAsync(ct);
        }
    }

    [Fact(DisplayName = "UpsertLocationAsync stamps a UUID v7 Id when the source Location's Id is unset")]
    public async Task UpsertLocationAsync_Should_StampUuidV7Id_When_SourceLocationHasUnsetId()
    {
        IntegrationDb.SkipIfUnavailable();
        using var context = NewContext();
        var ct = TestContext.Current.CancellationToken;
        var token = Guid.NewGuid().ToString("N");

        var dbCountry = new Country { Name = $"b06-country-{token}" };
        var dbProvider = new Provider { Name = $"b06-provider-{token}", ServiceName = "waldur", Country = dbCountry };
        var dbLocationType = new LocationType { Name = $"b06-loctype-{token}" };
        context.AddRange(dbProvider, dbLocationType);
        await context.SaveChangesAsync(ct);

        var location = new Location { Name = $"b06-loc-{token}", Endpoint = $"b06-{token}.example.com", ServiceName = "waldur" };
        Assert.Equal(Guid.Empty, location.Id);

        try
        {
            await context.UpsertLocationAsync(location, dbProvider, dbLocationType, ct);
            await context.SaveChangesAsync(ct);

            var persisted = await context.LocationsQuery.SingleAsync(l => l.Endpoint == location.Endpoint, ct);
            AssertStampedUuidV7(persisted.Id);
        }
        finally
        {
            await context.LocationsQuery.Where(l => l.Endpoint == location.Endpoint).ExecuteDeleteAsync(ct);
            await context.ProvidersQuery.Where(p => p.Name == dbProvider.Name).ExecuteDeleteAsync(ct);
            await context.LocationTypesQuery.Where(t => t.Name == dbLocationType.Name).ExecuteDeleteAsync(ct);
            await context.CountriesQuery.Where(c => c.Name == dbCountry.Name).ExecuteDeleteAsync(ct);
        }
    }

    [Fact(DisplayName = "UpsertProjectResourceAsync stamps a UUID v7 Id when the source ProjectResource's Id is unset")]
    public async Task UpsertProjectResourceAsync_Should_StampUuidV7Id_When_SourceResourceHasUnsetId()
    {
        IntegrationDb.SkipIfUnavailable();
        using var context = NewContext();
        var ct = TestContext.Current.CancellationToken;
        var token = Guid.NewGuid().ToString("N");

        var resource = new ProjectResource
        {
            Name = $"b06-resource-{token}",
            ServiceName = "waldur",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        Assert.Equal(Guid.Empty, resource.Id);

        try
        {
            var persisted = await context.UpsertProjectResourceAsync(resource, ct);
            await context.SaveChangesAsync(ct);

            AssertStampedUuidV7(persisted.Id);
        }
        finally
        {
            await context.ProjectResourcesQuery.Where(r => r.Name == resource.Name).ExecuteDeleteAsync(ct);
        }
    }

    [Fact(DisplayName = "UpsertAssignedLocationAsync stamps UUID v7 Ids for the source AssignedLocation and its AssignedLocationValues when both are unset")]
    public async Task UpsertAssignedLocationAsync_Should_StampUuidV7Ids_When_SourceIdsAreUnset()
    {
        IntegrationDb.SkipIfUnavailable();
        using var context = NewContext();
        var ct = TestContext.Current.CancellationToken;
        var token = Guid.NewGuid().ToString("N");

        var dbCountry = new Country { Name = $"b06-country-{token}" };
        var dbProvider = new Provider { Name = $"b06-provider-{token}", ServiceName = "waldur", Country = dbCountry };
        var dbLocationType = new LocationType { Name = $"b06-loctype-{token}" };
        var dbResource = new ProjectResource
        {
            Name = $"b06-resource-{token}",
            ServiceName = "waldur",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        var dbAllocationUnitType = new AllocationUnitType { Name = $"b06-alloctype-{token}" };
        context.AddRange(dbProvider, dbLocationType, dbResource, dbAllocationUnitType);
        await context.SaveChangesAsync(ct);

        var dbLocation = new Location
        {
            Name = $"b06-loc-{token}",
            Endpoint = $"b06-{token}.example.com",
            ServiceName = "waldur",
            ProviderId = dbProvider.Id,
            LocationTypeId = dbLocationType.Id
        };
        context.Add(dbLocation);
        await context.SaveChangesAsync(ct);

        var assignedLocationValue = new AssignedLocationValue { Name = "cores", Value = "16" };
        var assignedLocation = new AssignedLocation
        {
            ExternalId = $"b06-external-{token}",
            ServiceName = "waldur",
            Mode = EOperationMode.ReadOnly,
            Location = dbLocation,
            AssignedLocationsValues = [assignedLocationValue]
        };
        Assert.Equal(Guid.Empty, assignedLocation.Id);
        Assert.Equal(Guid.Empty, assignedLocationValue.Id);

        try
        {
            var persisted = await context.UpsertAssignedLocationAsync(assignedLocation, dbResource, dbAllocationUnitType, ct);
            await context.SaveChangesAsync(ct);

            AssertStampedUuidV7(persisted.Id);
            AssertStampedUuidV7(Assert.Single(persisted.AssignedLocationsValues).Id);
        }
        finally
        {
            await context.AssignedLocationValuesQuery.Where(v => v.AssignedLocation!.ExternalId == assignedLocation.ExternalId).ExecuteDeleteAsync(ct);
            await context.AssignedLocationsQuery.Where(al => al.ExternalId == assignedLocation.ExternalId).ExecuteDeleteAsync(ct);
            await context.LocationsQuery.Where(l => l.Endpoint == dbLocation.Endpoint).ExecuteDeleteAsync(ct);
            await context.ProjectResourcesQuery.Where(r => r.Name == dbResource.Name).ExecuteDeleteAsync(ct);
            await context.AllocationUnitTypesQuery.Where(a => a.Name == dbAllocationUnitType.Name).ExecuteDeleteAsync(ct);
            await context.ProvidersQuery.Where(p => p.Name == dbProvider.Name).ExecuteDeleteAsync(ct);
            await context.LocationTypesQuery.Where(t => t.Name == dbLocationType.Name).ExecuteDeleteAsync(ct);
            await context.CountriesQuery.Where(c => c.Name == dbCountry.Name).ExecuteDeleteAsync(ct);
        }
    }

    [Fact(DisplayName = "UpsertProjectAsync stamps a UUID v7 Id when the source Project's Id is unset")]
    public async Task UpsertProjectAsync_Should_StampUuidV7Id_When_SourceProjectHasUnsetId()
    {
        IntegrationDb.SkipIfUnavailable();
        using var context = NewContext();
        var ct = TestContext.Current.CancellationToken;
        var token = Guid.NewGuid().ToString("N");

        var createdByUser = new User { IdentityProviderUserId = $"b06-idp-{token}", Email = $"b06-{token}@example.com", UserName = $"b06-user-{token}" };
        context.Add(createdByUser);
        await context.SaveChangesAsync(ct);

        var project = new Project
        {
            Name = $"b06-project-{token}",
            ShortName = ProjectShortName.Create($"b06-{token}"),
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30),
            ProjectDomainType = new ProjectDomainType { Name = $"b06-domain-{token}" },
            ServiceName = "waldur",
            ExternalId = $"b06-external-{token}"
        };
        Assert.Equal(Guid.Empty, project.Id.Value);

        try
        {
            var persisted = await context.UpsertProjectAsync(NullLogger.Instance, "waldur", project, createdByUser, ct);
            await context.SaveChangesAsync(ct);

            AssertStampedUuidV7(persisted.Id.Value);
        }
        finally
        {
            await context.ProjectsQuery.Where(p => p.ExternalId == project.ExternalId).ExecuteDeleteAsync(ct);
            await context.ProjectDomainTypesQuery.Where(d => d.Name == project.ProjectDomainType.Name).ExecuteDeleteAsync(ct);
            await context.UsersQuery.Where(u => u.UserName == createdByUser.UserName).ExecuteDeleteAsync(ct);
        }
    }

    private static DatabaseContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseSqlServer(IntegrationDb.ConnectionString!)
            .Options;
        return new DatabaseContext(options);
    }

    /// <summary>
    /// A non-empty check alone would also pass on EF Core's own incidental Guid-key default, so the
    /// version pins the stamp to the shared <c>UuidV7ValueGenerator</c> specifically.
    /// </summary>
    private static void AssertStampedUuidV7(Guid id)
    {
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(7, id.Version);
    }
}
