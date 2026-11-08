namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Read DTO for one HEAppE resource validity audit target (ADR-0025): a currently-valid resource exposed on a
/// HEAppE instance, paired with the owning project's short name (for labelling) and the resource's own validity.
/// The HEAppE URL is returned as the raw stored string, or null when the location has none; the host normalizes it
/// to a <c>Uri</c> for logs only.
/// </summary>
/// <param name="ResourceName">The globally-unique resource name; also the key the HEAppE project record is addressed by.</param>
/// <param name="ProjectShortName">The owning project's short name — for labelling findings, not part of the match.</param>
/// <param name="HeappeUrl">The raw stored HEAPPE_URL value, or null when absent; only for logs.</param>
/// <param name="HeappeIdentifier">The HEAPPE_IDENTIFIER value; the audit routes by it and filters on it.</param>
/// <param name="StartDate">The validity start userorg holds for the resource.</param>
/// <param name="EndDate">The validity end userorg holds for the resource.</param>
public sealed record HeappeAuditTargetRow(
    string ResourceName,
    string ProjectShortName,
    string? HeappeUrl,
    string HeappeIdentifier,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);