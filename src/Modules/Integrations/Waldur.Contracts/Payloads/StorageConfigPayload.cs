using System.Collections.Immutable;

using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// The EFP storage-reconciliation configuration (ADR-0061). Hashed as a whole; the worker fires a
/// <see cref="Events.StorageConfigurationChanged"/> whenever this hash changes (and once
/// unconditionally after startup, since this slot is never cold-rebuilt from the DB — ADR-0055/A4).
/// </summary>
/// <param name="EfpCoreServiceName">Service name that owns reconciliation-created rows (excluded from all payload mapping).</param>
/// <param name="UseSharedIrodsForAllEntities">Whether the shared iRODS is associated to every resource.</param>
/// <param name="SharedIrods">The shared iRODS instance, if configured.</param>
/// <param name="IrodsInstances">Per-provider iRODS instances.</param>
/// <param name="StagingAreaInstances">Per-provider staging-area instances.</param>
[GenerateIdentityHash]
public sealed partial record StorageConfigPayload(
    string EfpCoreServiceName,
    bool UseSharedIrodsForAllEntities,
    StorageInstancePayload? SharedIrods,
    IImmutableList<StorageInstancePayload> IrodsInstances,
    IImmutableList<StorageInstancePayload> StagingAreaInstances);