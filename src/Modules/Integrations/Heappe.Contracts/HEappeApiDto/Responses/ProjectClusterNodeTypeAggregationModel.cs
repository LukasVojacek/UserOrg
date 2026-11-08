namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ProjectClusterNodeTypeAggregationModel
(
    long ProjectId,
    long ClusterNodeTypeAggregationId,
    long AllocationAmount,
    DateTime CreatedAt,
    DateTime? ModifiedAt
);