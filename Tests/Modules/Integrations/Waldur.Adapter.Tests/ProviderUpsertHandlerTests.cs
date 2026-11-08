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
/// Tests for the B07 <see cref="ProviderUpsertHandler"/>: the shared provider + derived-location
/// upsert routed from both <see cref="ProviderCreated"/> and <see cref="ProviderUpdated"/>
/// (ADR-0057/A1). Backed by the InMemory <see cref="FakeUserOrgDbContext"/>.
/// </summary>
public sealed class ProviderUpsertHandlerTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private static readonly string CustomerExternalId = Guid.NewGuid().ToString();

    private static ProviderPayload Payload(
        string name = "Acme HPC",
        string? country = "CZ",
        IImmutableList<ProviderLocationPayload>? locations = null) =>
        new(CustomerExternalId, name, country, "desc", locations ?? [Location()]);

    private static ProviderLocationPayload Location(
        string name = "karolina",
        string endpoint = "karolina.it4i.cz",
        string locationType = "HPC",
        bool isShared = true,
        bool isDeleted = false) =>
        new(name, endpoint, locationType, "loc-desc", isShared, isDeleted);

    private static ProviderCreated Created(ProviderPayload payload) =>
        new(payload) { Instance = Instance, ServiceName = ServiceName };

    private static ProviderUpdated Updated(ProviderPayload payload) =>
        new(payload) { Instance = Instance, ServiceName = ServiceName };

    private static ProviderUpsertHandler Handler(IUserOrgDbContext context) =>
        new(context, NullLogger<ProviderUpsertHandler>.Instance);

    /// <summary>Seeds the reference data (Country, LocationType) + service account the handler resolves.</summary>
    private static FakeUserOrgDbContext SeededContext(bool seedCountry = true, bool seedLocationType = true)
    {
        var context = new FakeUserOrgDbContext();
        if (seedCountry)
        {
            context.Seed<Country>([new Country { Id = 1, Name = "CZ" }]);
        }

        if (seedLocationType)
        {
            context.Seed<LocationType>([new LocationType { Id = 1, Name = "HPC" }]);
        }

        context.Seed<User>([new User
        {
            Id = Guid.CreateVersion7(),
            IdentityProviderUserId = ServiceName,
            Email = "svc@waldur",
            UserName = ServiceName,
            CreatedAt = DateTime.UtcNow,
        }]);
        return context;
    }

    [Fact(DisplayName = "Creates the provider keyed (ExternalId, ServiceName) when absent")]
    public async Task Should_CreateProvider_When_Absent()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        var provider = await context.ProvidersQuery
            .SingleAsync(p => p.ExternalId == CustomerExternalId && p.ServiceName == ServiceName);
        Assert.Equal("Acme HPC", provider.Name);
    }

    [Fact(DisplayName = "Updates the existing provider (no duplicate) when present")]
    public async Task Should_UpdateProvider_When_Present()
    {
        using var context = SeededContext();
        await Handler(context).HandleAsync(Created(Payload(name: "Old Name")), CancellationToken.None);

        await Handler(context).HandleAsync(Updated(Payload(name: "New Name")), CancellationToken.None);

        var provider = await context.ProvidersQuery
            .SingleAsync(p => p.ExternalId == CustomerExternalId && p.ServiceName == ServiceName);
        Assert.Equal("New Name", provider.Name);
    }

    [Fact(DisplayName = "Upserts the derived location for the provider")]
    public async Task Should_UpsertDerivedLocation_When_ProviderUpserted()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        var location = await context.LocationsQuery
            .SingleAsync(l => l.Endpoint == "karolina.it4i.cz" && l.ServiceName == ServiceName);
        Assert.Equal("karolina", location.Name);
    }

    [Fact(DisplayName = "Reuses a SAME-provider endpoint-only location (fallback) instead of duplicating")]
    public async Task Should_ReuseEndpointOnlyLocation_When_ServiceNameDiffers()
    {
        using var context = SeededContext();
        // First upsert (no locations) so the provider exists with a known Id.
        await Handler(context).HandleAsync(Created(Payload(locations: [])), CancellationToken.None);
        var providerId = (await context.ProvidersQuery
            .SingleAsync(p => p.ExternalId == CustomerExternalId && p.ServiceName == ServiceName)).Id;

        // A pre-existing location UNDER THIS PROVIDER with the same endpoint but a different service.
        context.Seed<Location>([new Location
        {
            Id = Guid.CreateVersion7(),
            Name = "pre-existing",
            Endpoint = "karolina.it4i.cz",
            ServiceName = "OTHER-SERVICE",
            ProviderId = providerId,
            LocationTypeId = 1,
            CreatedAt = DateTime.UtcNow,
        }]);

        await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        var locations = await context.LocationsQuery
            .Where(l => l.ProviderId == providerId && l.Endpoint == "karolina.it4i.cz")
            .ToListAsync();
        Assert.Single(locations);                       // endpoint-only fallback reused it — no duplicate
        Assert.Equal("karolina", locations[0].Name);    // SyncFrom updated the name
    }

    [Fact(DisplayName = "Does NOT touch a location owned by a different provider (review 2a)")]
    public async Task Should_NotMutateForeignProviderLocation_When_EndpointMatches()
    {
        using var context = SeededContext();
        var foreignProviderId = Guid.CreateVersion7();
        context.Seed<Location>([new Location
        {
            Id = Guid.CreateVersion7(),
            Name = "foreign",
            Endpoint = "karolina.it4i.cz",
            ServiceName = "OTHER-SERVICE",
            ProviderId = foreignProviderId,
            LocationTypeId = 1,
            CreatedAt = DateTime.UtcNow,
        }]);

        await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        var all = await context.LocationsQuery.Where(l => l.Endpoint == "karolina.it4i.cz").ToListAsync();
        Assert.Equal(2, all.Count);                                        // foreign untouched + new one under this provider
        Assert.Equal("foreign", all.Single(l => l.ProviderId == foreignProviderId).Name);   // not mutated
    }

    [Fact(DisplayName = "Returns failure (not acked) when the endpoint is ambiguous within the provider")]
    public async Task Should_ReturnFailure_When_LocationAmbiguous()
    {
        using var context = SeededContext();
        await Handler(context).HandleAsync(Created(Payload(locations: [])), CancellationToken.None);
        var providerId = (await context.ProvidersQuery
            .SingleAsync(p => p.ExternalId == CustomerExternalId && p.ServiceName == ServiceName)).Id;
        context.Seed<Location>(
        [
            new Location { Id = Guid.CreateVersion7(), Name = "a", Endpoint = "karolina.it4i.cz", ServiceName = "A", ProviderId = providerId, LocationTypeId = 1, CreatedAt = DateTime.UtcNow },
            new Location { Id = Guid.CreateVersion7(), Name = "b", Endpoint = "karolina.it4i.cz", ServiceName = "B", ProviderId = providerId, LocationTypeId = 1, CreatedAt = DateTime.UtcNow },
        ]);

        var result = await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact(DisplayName = "Returns failure (not acked) when the Country is missing from the DB")]
    public async Task Should_ReturnFailure_When_CountryMissing()
    {
        using var context = SeededContext(seedCountry: false);

        var result = await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await context.ProvidersQuery.ToListAsync()); // terminal SaveChanges never reached
    }

    [Fact(DisplayName = "Returns failure (not acked) when the LocationType is missing from the DB")]
    public async Task Should_ReturnFailure_When_LocationTypeMissing()
    {
        using var context = SeededContext(seedLocationType: false);

        var result = await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await context.ProvidersQuery.ToListAsync());
    }

    [Fact(DisplayName = "Returns failure (not acked) when the service account is not bootstrapped")]
    public async Task Should_ReturnFailure_When_ServiceAccountMissing()
    {
        using var context = new FakeUserOrgDbContext();
        context.Seed<Country>([new Country { Id = 1, Name = "CZ" }]);
        context.Seed<LocationType>([new LocationType { Id = 1, Name = "HPC" }]);

        var result = await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await context.ProvidersQuery.ToListAsync());
    }

    [Fact(DisplayName = "ProviderCreated and ProviderUpdated route to the same upsert behaviour")]
    public async Task Should_RouteBothEvents_To_SameUpsert()
    {
        using var createdContext = SeededContext();
        using var updatedContext = SeededContext();

        await Handler(createdContext).HandleAsync(Created(Payload(name: "Same")), CancellationToken.None);
        await Handler(updatedContext).HandleAsync(Updated(Payload(name: "Same")), CancellationToken.None);

        var fromCreated = await createdContext.ProvidersQuery.SingleAsync(p => p.ExternalId == CustomerExternalId);
        var fromUpdated = await updatedContext.ProvidersQuery.SingleAsync(p => p.ExternalId == CustomerExternalId);
        Assert.Equal(fromCreated.Name, fromUpdated.Name);
        Assert.Equal(EOperationMode.ReadOnly, fromCreated.Mode);
    }

    [Fact(DisplayName = "Handler is registered via the B06 seam for both provider events")]
    public void Should_RegisterHandler_For_BothProviderEvents()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddLogging();
        services.AddWaldurEventConsumers();
        services.AddWaldurProviderHandler();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<ProviderCreated>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<ProviderUpdated>>());
    }
}
