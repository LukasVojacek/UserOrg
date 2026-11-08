using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

/// <summary>Raw Waldur <c>/api/marketplace-resources/?project_uuid=…</c> item.</summary>
public sealed record ApiMarketplaceResource(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("project_uuid")] Guid ProjectId,
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("offering_uuid")] Guid OfferingId,
    [property: JsonPropertyName("offering_name")] string OfferingName,
    [property: JsonPropertyName("limits")] IReadOnlyDictionary<string, decimal>? Allocations,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("end_date")] DateTimeOffset? EndDate,
    [property: JsonPropertyName("created")] DateTimeOffset? Created,
    [property: JsonPropertyName("modified")] DateTimeOffset? Modified)
{
    [JsonPropertyName("backend_id")]
    public string? BackendId
    {
        get => field;
        init => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [JsonPropertyName("effective_id")]
    public string? EffectiveId
    {
        get => field;
        init => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }
}