using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

/// <summary>Raw Waldur <c>/api/customers/</c> item.</summary>
public sealed record ApiCustomer(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("created")] DateTimeOffset Created);