using Heappe.Persistence.Enums;

namespace Heappe.Features.CommandTemplatePermissions;

/// <summary>
/// One materialized command-template partition row — the shape the permission query reads out of the
/// database before the response is composed.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project to which the command template belongs.</param>
/// <param name="ProjectCommandTemplateId">The unique identifier of the command template the partition belongs to.</param>
/// <param name="CommandTemplateName">The name of the command template associated with this partition.</param>
/// <param name="CommandTemplateState">The stored state of the command template on this partition.</param>
/// <param name="LocationId">The unique identifier of the location associated with this partition.</param>
/// <param name="ProjectResourceId">The unique identifier of the the project resource.</param>
/// <param name="AggregationName">The name of the aggregation context, if applicable; otherwise, null.</param>
/// <param name="Partition">The partition key or identifier for this command template instance.</param>
internal sealed record CommandTemplatePartitionRow
(
    Guid ProjectId,
    Guid ProjectCommandTemplateId,
    string CommandTemplateName,
    ECommandTemplateState CommandTemplateState,
    Guid LocationId,
    Guid ProjectResourceId,
    string? AggregationName,
    string Partition
);