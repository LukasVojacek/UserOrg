using UserOrg.Shared;

namespace Heappe.Contracts.Entries.Init;

public sealed record HeappeProjectSpecificationEntry
(
    Uri? HeappeUri,
    HeappeIdentifier HeappeIdentifier,
    ProjectSpecificationEntry ProjectSpecification,
    ClusterSpecificationEntry ClusterSpecification
);