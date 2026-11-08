namespace Heappe.Contracts.Entries.Init;

public sealed record AllocationSpecificationEntry(
    string? NodeTypeAggregationName,
    long AllocationAmount
);