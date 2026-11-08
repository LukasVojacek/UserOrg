using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.DataAccess.Outbox;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Real-DB proof of the transactional-outbox atomicity guarantee (ADR-0028, outbox-flow.md §3): the
/// aggregate change and its <see cref="ResourceAssignmentChangedEvent"/> outbox row commit — or roll
/// back — <b>together</b>, over a single <see cref="System.Data.Common.DbConnection"/> enlisted in one
/// transaction (<c>DatabaseContext.UnitOfWork</c> BeginTransaction → save aggregate → enlist
/// <see cref="OutboxDbContext"/> via <c>UseTransaction</c> → INSERT OutboxMessage → Commit).
///
/// This CANNOT be exercised on the EF Core in-memory provider: in-memory has no real
/// <c>DbConnection</c> and no <c>UseTransaction</c>, so a second context enlisting on the first
/// context's transaction is a no-op there. The cross-context, same-connection commit is exactly the
/// thing this test pins, so it needs a real SQL Server — gated on <see cref="IntegrationDb.EnvVar"/>
/// and skipped (never failed) when it is unset.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OutboxTransactionalIntegrationTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;

    public OutboxTransactionalIntegrationTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.AddCoreDataAccess(configuration);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_CommitAggregateAndOutboxRowTogether_When_DurableEventRaised()
    {
        IntegrationDb.SkipIfUnavailable();
        var connectionString = IntegrationDb.ConnectionString!;
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.NewGuid().ToString("N");
        var providerName = ProviderName.From($"b03-prov-{token}");
        var resourceName = ResourceName.Create($"b03-res-{token}");
        var locationName = LocationName.From("b03-loc");
        var seed = new ReferenceSeed(token);

        try
        {
            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var (countryId, allocationUnitTypeId, locationType) = await SeedReferenceDataAsync(context, seed, ct);

                var provider = ProviderAggregate.Create(providerName, countryId);
                Assert.True(provider.AddLocation(locationName, "https://b03", locationType, isShared: true).IsSuccess);
                var resource = ProjectResource.Create(resourceName, provider, UserId.From(Guid.NewGuid()), Start, End);
                var assignment = provider.AssignResource(
                    locationName, resource, allocationUnitTypeId, allocationUnitCount: null,
                    description: null, aggregationName: null, specifications: NoSpecs,
                    resourceHasNonSharedLocation: false, slug: $"b03-asg-{token}");
                Assert.True(assignment.IsSuccess); // raises ResourceAssignmentChangedEvent : IDurableEvent

                // Both roots go in through their repositories: the IUnitOfWork save path refuses to INSERT
                // an AggregateRoot that no repository AddAsync ever registered (T-055-03 detached-aggregate
                // guard). Adding them straight onto the context would trip that guard here.
                await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);
                await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

                // The DURABLE path. Calling through IUnitOfWork (not the plain DbContext.SaveChanges)
                // routes the durable event to the outbox in the SAME transaction as the aggregate insert.
                await ((IUnitOfWork)context).SaveChangesAsync(ct);
            }

            // The aggregate row is committed.
            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var persisted = await context.Set<ProviderAggregate>()
                    .FirstOrDefaultAsync(p => p.Name == providerName, ct);
                Assert.NotNull(persisted);
            }

            // The outbox row is committed in that same transaction — pending, unattempted.
            await using (var outbox = NewOutboxContext(connectionString))
            {
                var eventType = typeof(ResourceAssignmentChangedEvent).FullName!;
                var row = await outbox.OutboxMessages.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Type == eventType && m.Payload.Contains(token), ct);

                Assert.NotNull(row);
                Assert.Null(row.DispatchedAt);
                Assert.Equal(0, row.Attempts);
            }
        }
        finally
        {
            await CleanupAsync(connectionString, providerName, resourceName, token, seed, ct);
        }
    }

    [Fact]
    public async Task Should_RollBackAggregate_When_OutboxInsertFailsInSameTransaction()
    {
        IntegrationDb.SkipIfUnavailable();
        var connectionString = IntegrationDb.ConnectionString!;
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.NewGuid().ToString("N");
        var providerName = ProviderName.From($"b03-neg-{token}");
        var resourceName = ResourceName.Create($"b03-negres-{token}");
        var locationName = LocationName.From("b03-loc");
        var seed = new ReferenceSeed(token);

        // This is the LOAD-BEARING direction B03 exists to pin: force the OUTBOX INSERT (not the aggregate
        // insert) to fail, AFTER the aggregate has already been saved within the durable transaction, and
        // prove the aggregate is rolled back. That can only happen if both writes share ONE transaction — if
        // the outbox committed in a SEPARATE transaction the aggregate would survive. The aggregate insert
        // here is perfectly valid (real Country FK); only the outbox row is rejected.
        //
        // Data alone cannot force it: Payload is nvarchar(max) and Type carries a fixed FullName, and the row
        // Id is a fresh Guid — nothing to overflow or collide. So the test ARRANGES a WITH NOCHECK CHECK
        // constraint that rejects the new (Attempts = 0) outbox row while leaving existing pending rows
        // untouched. It is a test-owned DB constraint, dropped in the finally — no production seam.
        var constraintName = $"CK_B03_ForceOutboxFail_{token}";
        await AddOutboxRejectConstraintAsync(connectionString, constraintName, ct);

        try
        {
            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var (countryId, allocationUnitTypeId, locationType) = await SeedReferenceDataAsync(context, seed, ct);

                var provider = ProviderAggregate.Create(providerName, countryId);
                Assert.True(provider.AddLocation(locationName, "https://b03", locationType, isShared: true).IsSuccess);
                var resource = ProjectResource.Create(resourceName, provider, UserId.From(Guid.NewGuid()), Start, End);
                var assignment = provider.AssignResource(
                    locationName, resource, allocationUnitTypeId, allocationUnitCount: null,
                    description: null, aggregationName: null, specifications: NoSpecs,
                    resourceHasNonSharedLocation: false, slug: $"b03-negasg-{token}");
                Assert.True(assignment.IsSuccess); // raises the durable event → forces the outbox INSERT

                // Both roots go in through their repositories: the IUnitOfWork save path refuses to INSERT
                // an AggregateRoot that no repository AddAsync ever registered (T-055-03 detached-aggregate
                // guard). Adding them straight onto the context would trip that guard here.
                await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);
                await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

                // The aggregate save succeeds inside the tx; the outbox INSERT then violates the check
                // constraint and the whole transaction is rolled back.
                await Assert.ThrowsAnyAsync<DbUpdateException>(
                    async () => await ((IUnitOfWork)context).SaveChangesAsync(ct));
            }

            // The aggregate is GONE — rolled back because the outbox write in the same transaction failed.
            await using (var scope = _serviceProvider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var persisted = await context.Set<ProviderAggregate>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Name == providerName, ct);
                Assert.Null(persisted);
            }

            // And no outbox row landed either.
            await using (var outbox = NewOutboxContext(connectionString))
            {
                var row = await outbox.OutboxMessages.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Payload.Contains(token), ct);
                Assert.Null(row);
            }
        }
        finally
        {
            await DropOutboxConstraintAsync(connectionString, constraintName, ct);
            await CleanupAsync(connectionString, providerName, resourceName, token, seed, ct);
        }
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static OutboxDbContext NewOutboxContext(string connectionString) =>
        new(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlServer(connectionString)
            .Options);

    // WITH NOCHECK: skip validation of existing (possibly pending) rows, but still reject every new INSERT —
    // a new outbox row has Attempts = 0, which can never equal -1. The constraint name is a Guid-derived hex,
    // so the interpolated DDL carries no injection surface.
    private static async Task AddOutboxRejectConstraintAsync(string connectionString, string constraintName, CancellationToken ct)
    {
        // constraintName is a Guid-derived hex identifier (no injection surface); it cannot be a SQL
        // parameter because it is a DDL object name, so EF1002 is suppressed for these test-only statements.
        var sql = $"ALTER TABLE [outbox].[Outbox] WITH NOCHECK ADD CONSTRAINT [{constraintName}] CHECK ([Attempts] = -1);";
        await using var outbox = NewOutboxContext(connectionString);
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection — DDL identifier is a controlled Guid hex.
        await outbox.Database.ExecuteSqlRawAsync(sql, ct);
#pragma warning restore EF1002
    }

    private static async Task DropOutboxConstraintAsync(string connectionString, string constraintName, CancellationToken ct)
    {
        var sql = $"IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = '{constraintName}') ALTER TABLE [outbox].[Outbox] DROP CONSTRAINT [{constraintName}];";
        await using var outbox = NewOutboxContext(connectionString);
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection — DDL identifier is a controlled Guid hex.
        await outbox.Database.ExecuteSqlRawAsync(sql, ct);
#pragma warning restore EF1002
    }

    // why: on a database migrated from zero none of these reference tables carries a row, so reading what is
    // why: there would turn both tests into vacuous skips; what the fixture seeds, CleanupAsync deletes
    private static async Task<(int CountryId, int AllocationUnitTypeId, ELocationType LocationType)> SeedReferenceDataAsync(
        DatabaseContext context, ReferenceSeed seed, CancellationToken ct)
    {
        var (countryId, _) = await seed.SeedLookupAsync(context, "Country", ct);
        var (allocationUnitTypeId, _) = await seed.SeedLookupAsync(context, "AllocationUnitType", ct);
        var locationType = await seed.SeedLocationTypeAsync(context, ELocationType.Quantum, ct);

        return (countryId, allocationUnitTypeId, locationType);
    }

    private async Task CleanupAsync(
        string connectionString, ProviderName providerName, ResourceName resourceName, string token,
        ReferenceSeed seed, CancellationToken ct)
    {
        // Remove the outbox row so the live dispatcher never tries to deliver this throwaway fixture to HEAppE.
        await using (var outbox = NewOutboxContext(connectionString))
        {
            await outbox.OutboxMessages.Where(m => m.Payload.Contains(token)).ExecuteDeleteAsync(ct);
        }

        // Hard-delete the fixture graph. The plain DbContext.SaveChanges path (NOT IUnitOfWork) skips the
        // soft-delete conversion, so Remove issues real DELETEs; EF orders them by FK dependency within the
        // one save (AssignedLocation → Location / ProjectResource → Provider).
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = await context.Set<ProviderAggregate>().IgnoreQueryFilters()
            .Include("_providerLocations._locationAllocations")
            .FirstOrDefaultAsync(p => p.Name == providerName, ct);
        var resource = await context.Set<ProjectResource>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ResourceName == resourceName, ct);

        if (provider is not null)
        {
            DeleteProviderLocationGraph(context, provider);
            context.Remove(provider);
        }

        if (resource is not null)
        {
            context.Remove(resource);
        }

        if (provider is not null || resource is not null)
        {
            await context.SaveChangesAsync(ct);
        }

        await seed.DeleteSeededAsync(context);
    }

    // why: the children hang off private backing fields, so they are deleted through the tracker; removing only
    // why: the root severs a required relationship instead of cascading (FKs here are NO ACTION)
    // warn: walk this provider's own navigations — every tracked Location in the scope would delete rows the
    // warn: fixture never created the moment anything else in the scope loads one
    private static void DeleteProviderLocationGraph(DatabaseContext context, ProviderAggregate provider)
    {
        var providerEntry = context.Entry(provider);
        var locations = providerEntry.Collection("_providerLocations").CurrentValue?.OfType<Location>() ?? [];

        foreach (var location in locations.ToList())
        {
            var locationEntry = context.Entry(location);
            var allocations = locationEntry.Collection("_locationAllocations").CurrentValue?.OfType<AssignedLocation>() ?? [];

            foreach (var allocation in allocations.ToList())
            {
                context.Entry(allocation).State = EntityState.Deleted;
            }

            locationEntry.State = EntityState.Deleted;
        }
    }
}
