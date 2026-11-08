using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

/// <summary>Raw Waldur <c>/api/projects/</c> item.</summary>
public sealed record ApiProject(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("start_date")] DateTimeOffset? StartDate,
    [property: JsonPropertyName("end_date")] DateTimeOffset? EndDate,
    [property: JsonPropertyName("created")] DateTimeOffset? Created,
    [property: JsonPropertyName("is_removed")] bool IsRemoved);