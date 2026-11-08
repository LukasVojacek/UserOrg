namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ClusterNodeTypeAggregationModel
(
    long Id,
    string Name,
    string Description,
    string AllocationType,
    DateTime ValidityFrom,
    DateTime? ValidityTo
)
{
    public static ClusterNodeTypeAggregationModel Empty => new(
        Id: 0,
        Name: string.Empty,
        Description: string.Empty,
        AllocationType: string.Empty,
        ValidityFrom: DateTime.MinValue,
        ValidityTo: null
    );
}