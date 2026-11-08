namespace Waldur.Sync.Diff;

/// <summary>
/// Startup cold-rebuild port (ADR-0055/D1): reconstructs an instance's SHA breadcrumb map from the
/// UserOrg database (DB → payloads → hash). Implemented adapter-side (B11) so <c>Waldur.Sync</c>
/// never sees the DB. Implementations MUST hash via the payload's own
/// <see cref="UserOrg.Shared.Hashing.IIdentityHashed.ComputeIdentityHash"/> and MUST return
/// only cold-rebuildable keys — the storage-config slot is excluded (ADR-0055/A4,
/// see <see cref="ShaKey.IsColdRebuildable"/>): it has no DB source of truth, so the worker fires one
/// unconditional <c>StorageConfigurationChanged</c> after startup instead. The rebuild also excludes
/// reconciliation-owned rows and includes soft-deleted projects (A4).
/// </summary>
public interface IShaMapRebuilder
{
    /// <summary>Rebuilds the SHA map for <paramref name="instance"/> from the database.</summary>
    Task<IReadOnlyDictionary<ShaKey, string>> RebuildAsync(string instance, CancellationToken ct);
}