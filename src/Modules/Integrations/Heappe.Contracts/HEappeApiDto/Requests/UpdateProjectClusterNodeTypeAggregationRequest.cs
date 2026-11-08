namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record UpdateProjectClusterNodeTypeAggregationRequest
(
    long ClusterNodeTypeAggregationId,
    long ProjectId,
    long AllocationAmount
);