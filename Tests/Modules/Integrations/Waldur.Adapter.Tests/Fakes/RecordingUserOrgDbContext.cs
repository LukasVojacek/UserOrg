using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace Waldur.Adapter.Tests.Fakes;

internal sealed class RecordingUserOrgDbContext : DatabaseContext
{
    private bool captureSaveSnapshots;

    public RecordingUserOrgDbContext()
        : base(new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options)
    {
    }

    public List<IReadOnlyList<TrackedEntrySnapshot>> SaveSnapshots { get; } = [];

    public void BeginCapture()
    {
        captureSaveSnapshots = true;
        SaveSnapshots.Clear();
    }

    public void Seed<T>(IEnumerable<T> entities) where T : class
    {
        AddRange(entities);
        SaveChanges();
        ChangeTracker.Clear();
    }

    public override int SaveChanges()
    {
        CaptureSnapshotIfEnabled();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CaptureSnapshotIfEnabled();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void CaptureSnapshotIfEnabled()
    {
        if (!captureSaveSnapshots)
        {
            return;
        }

        SaveSnapshots.Add(ChangeTracker.Entries()
            .Select(CreateSnapshot)
            .ToArray());
    }

    private static TrackedEntrySnapshot CreateSnapshot(EntityEntry entry)
    {
        var primaryKey = entry.Metadata.FindPrimaryKey();
        var primaryKeyValue = primaryKey is null
            ? null
            : string.Join(
                "|",
                primaryKey.Properties.Select(property => entry.Property(property.Name).CurrentValue?.ToString() ?? "<null>"));

        return new TrackedEntrySnapshot(
            entry.Metadata.ClrType.Name,
            entry.State,
            primaryKeyValue);
    }
}

internal sealed record TrackedEntrySnapshot(
    string EntityType,
    EntityState State,
    string? PrimaryKeyValue);
