using UserOrg.Shared;

namespace Heappe.Contracts.Entries.Audit;

/// <summary>
/// One resource-at-an-instance the validity audit must verify: a currently-valid resource assigned at an
/// HPC or Quantum location that carries a HEAppE identifier. Produced by the host read side, consumed by the audit.
/// </summary>
/// <param name="ResourceName">The globally-unique resource name; also the key the HEAppE project record is addressed by.</param>
/// <param name="ProjectShortName">The owning project's short name — for labelling findings, not part of the match.</param>
/// <param name="HeappeUri">The HEAppE instance URL the resource is exposed on; only for logs, calls are routed by the identifier.</param>
/// <param name="HeappeIdentifier">The HEAppE instance identifier used for routing and to resolve service credentials.</param>
/// <param name="StartDate">The validity start userorg holds for the resource (the value HEAppE should carry).</param>
/// <param name="EndDate">The validity end userorg holds for the resource.</param>
public sealed record HeappeAuditTargetEntry(
    string ResourceName,
    string ProjectShortName,
    Uri? HeappeUri,
    HeappeIdentifier HeappeIdentifier,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);