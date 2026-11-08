using System.Data.Common;

using Core.DataAccess.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Core.DataAccess.Contexts;

/// <summary>
/// IUnitOfWork implementation on DatabaseContext.
/// After a successful SaveChanges, collects domain events from all tracked AggregateRoot
/// entities and writes them to DomainEventChannel for background processing.
/// </summary>
internal sealed partial class DatabaseContext : IUnitOfWork
{
    // Cached OutboxDbContext options, keyed on the reference identity of the DbConnection instance that
    // built them. DatabaseContext is registered scoped and non-pooled (ServiceCollectionExtensions.cs),
    // so Database.GetDbConnection() returns the SAME DbConnection instance across close/reopen within a
    // scope — identity keying (not open/closed state) is what keeps the cache valid and cannot swap the
    // connection enlisted in the open transaction.
    private DbConnection? _outboxOptionsConnection;
    private DbContextOptions<OutboxDbContext>? _outboxOptions;

    /// <summary>
    /// Persists all pending changes and dispatches domain events.
    /// Collects events from all tracked AggregateRoot entities before save,
    /// then writes them to the DomainEventChannel after a successful save.
    /// </summary>
    async Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        var events = CollectDomainEvents();

        // Detached-aggregate guard (T-055-03): ahead of ApplyConventions and ahead of BOTH save branches
        // below, so a cascaded insert is rejected before the durable-outbox branch even opens a transaction.
        GuardAgainstDetachedAggregateInserts();

        // Apply soft-delete interception and shadow property stamping
        ApplyConventions();

        // Route events: durable events go to the transactional outbox in the SAME transaction as the
        // aggregate change (ADR-0028); best-effort events stay on the at-most-once in-memory channel.
        var (durable, bestEffort) = OutboxRouting.Partition(events);

        int result;
        if (durable.Count == 0)
        {
            // Common case — no durable events: keep the current single implicit-transaction path.
            result = await base.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Atomic write: aggregate change + outbox rows commit (or roll back) together. The outbox
            // is owned by a separate context (ADR-0041 schema isolation); both contexts share this
            // context's open connection and one transaction (ADR-0028 atomicity).
            await using var tx = await Database.BeginTransactionAsync(cancellationToken);

            result = await base.SaveChangesAsync(cancellationToken);

            var outboxOptions = GetOutboxOptions();
            await using var outbox = new OutboxDbContext(outboxOptions);
            await outbox.Database.UseTransactionAsync(tx.GetDbTransaction(), cancellationToken);

            outbox.OutboxMessages.AddRange(durable.Select(OutboxRouting.ToOutboxMessage));
            await outbox.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }

        // The registration set is per-save, not per-context: an aggregate registered for THIS save must
        // not license a bypass of the guard on a later save through the same (scoped) context instance.
        _registeredAggregateRoots.Clear();

        // Best-effort dispatch happens only after the save/commit; durable events are NEVER written to
        // the channel (their delivery is owned by the outbox dispatcher, ADR-0040).
        await DispatchEventsAsync(bestEffort, cancellationToken);

        return result;
    }

    /// <summary>
    /// Detached-aggregate guard (T-055-03). A graph carried across scopes (an aggregate loaded in one scope
    /// and reused, untracked, in the next) can get cascade-marked <see cref="EntityState.Added"/> by
    /// <see cref="ChangeTracker.DetectChanges"/> when something navigates to it — EF would then silently
    /// INSERT a duplicate root nobody asked for. The distinguishing signature is not the key (client-generated
    /// UUID v7 by design, ADR-0061) but whether the aggregate went through a repository's <c>AddAsync</c> in
    /// THIS unit of work: every legitimate creation path registers its root via
    /// <see cref="RegisterAddedAggregateRoot"/> before this guard runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A tracked <see cref="AggregateRoot"/> is in <see cref="EntityState.Added"/> without having been
    /// registered through its repository's <c>AddAsync</c> — a cascaded/detached insert.
    /// </exception>
    private void GuardAgainstDetachedAggregateInserts()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (_registeredAggregateRoots.Contains(entry.Entity))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"A '{entry.Entity.GetType().Name}' aggregate root is about to be inserted but was never added through its repository in this unit of work. This usually means a detached instance was reached via navigation from another aggregate and got cascade-marked Added — load it through its repository in the current scope instead of carrying it across scopes.");
        }
    }

    /// <summary>
    /// Returns the cached <see cref="OutboxDbContext"/> options for the current <see cref="DbConnection"/>
    /// instance, building them once per connection identity. Keyed on reference identity, not on
    /// open/closed state, so a close/reopen of the same underlying connection (this context is scoped and
    /// non-pooled) still hits the cache.
    /// Internal (not private) so a unit test can prove the cache hit by reference identity of the
    /// returned options, without driving a real transactional save.
    /// </summary>
    internal DbContextOptions<OutboxDbContext> GetOutboxOptions()
    {
        var connection = Database.GetDbConnection();
        if (_outboxOptions is null || !ReferenceEquals(_outboxOptionsConnection, connection))
        {
            _outboxOptionsConnection = connection;
            _outboxOptions = new DbContextOptionsBuilder<OutboxDbContext>()
                .UseSqlServer(connection)
                .Options;
        }

        return _outboxOptions;
    }

    private void ApplyConventions()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletableEntity)
            {
                entry.State = EntityState.Modified;
                entry.Property("IsDeleted").CurrentValue = true;
            }

            if (entry.State == EntityState.Modified)
            {
                var modifiedAt = entry.Metadata.FindProperty("ModifiedAt");
                if (modifiedAt is not null)
                {
                    entry.Property("ModifiedAt").CurrentValue = DateTimeOffset.UtcNow;
                }
            }
        }
    }

    private List<IDomainEvent> CollectDomainEvents()
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        return events;
    }

    private async Task DispatchEventsAsync(List<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        foreach (var evt in events)
        {
            await _domainEventChannel.Writer.WriteAsync(evt, cancellationToken);
        }
    }

    /// <summary>
    /// Gets a repository for the specified aggregate type.
    /// DatabaseContext must implement IRepository<TAggregate, TKey> for the requested aggregate.
    /// Uses runtime type checking to ensure the aggregate is registered.
    /// </summary>
    /// <exception cref="NotImplementedException">
    /// Thrown if DatabaseContext does not implement IRepository for the requested aggregate.
    /// </exception>
    public IRepository<TAggregate, TKey> GetRepository<TAggregate, TKey>() where TAggregate : AggregateRoot
    {
        if (this is IRepository<TAggregate, TKey> repository)
        {
            return repository;
        }

        throw new NotImplementedException(
            $"Repository for aggregate '{typeof(TAggregate).Name}' with key '{typeof(TKey).Name}' is not implemented in {nameof(DatabaseContext)}.");
    }
}