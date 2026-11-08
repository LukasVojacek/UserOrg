using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// One configured storage location (iRODS or staging area) from the EFP instance options. Part of
/// <see cref="StorageConfigPayload"/>; changes to any field flip the storage-config hash and drive a
/// <see cref="Events.StorageConfigurationChanged"/> reconcile (ADR-0061).
/// </summary>
/// <param name="HostingEntityExternalId">Provider UUID this instance is bound to; null for the shared iRODS.</param>
/// <param name="Name">Location name.</param>
/// <param name="Endpoint">Location endpoint.</param>
/// <param name="LocationType">"iRODS" or "Storage".</param>
/// <param name="AllocationAmount">Allocation amount granted to associated resources, if any.</param>
[GenerateIdentityHash]
public sealed partial record StorageInstancePayload(
    string? HostingEntityExternalId,
    string Name,
    string Endpoint,
    string LocationType,
    int? AllocationAmount);