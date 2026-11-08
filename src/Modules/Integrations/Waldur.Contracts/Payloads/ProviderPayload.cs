using System.Collections.Immutable;

using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// Fat payload for a provider (Waldur customer). Upsert key is <c>(ExternalId, ServiceName)</c>;
/// <see cref="Locations"/> is derived from the customer's offerings (ADR-0054/A3) and is part of
/// the hash.
/// </summary>
/// <param name="ExternalId">Waldur customer UUID (canonical lowercase "D" form).</param>
/// <param name="Name">Customer name.</param>
/// <param name="Country">Country name; resolved to a <c>Country</c> entity by the consumer.</param>
/// <param name="Description">Optional description (truncated in mapping).</param>
/// <param name="Locations">Derived provider locations.</param>
/// <param name="CreatedAt">Audit timestamp — travels for the create path only, never hashed (A1).</param>
[GenerateIdentityHash]
public sealed partial record ProviderPayload(
    string ExternalId,
    string Name,
    string? Country,
    string? Description,
    IImmutableList<ProviderLocationPayload> Locations)
{
    [HashIgnore]
    public DateTime? CreatedAt { get; init; }
}