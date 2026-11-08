namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record CreateProjectClusterNodeTypeAggregationRequest
(
    long ClusterNodeTypeAggregationId,
    long ProjectId,
    long AllocationAmount
);