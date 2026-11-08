namespace Heappe.Features.CreateProjectCommandTemplate.Response;

/// <summary>
/// Represents the command template creation response.
/// </summary>
/// <param name="PartitionName">The partition name.</param>
/// <param name="AggregationName">The aggregation/queue name.</param>
/// <param name="LocationName">The location name.</param>
public sealed record CommandTemplateCreationResponse(
    string PartitionName,
    string AggregationName,
    string LocationName
);