using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

public sealed record ApiPublicOfferingComponent(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("measured_unit")] string MeasuredUnit,
    [property: JsonPropertyName("description")] string? Description);