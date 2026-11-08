namespace Heappe.Contracts.Entries.Init;

public sealed record ClusterSpecificationEntry(
    string LoginNodeName,
    string ProjectStoragePath,
    string ScratchStoragePath,
    AllocationSpecificationEntry AllocationSpecification
);