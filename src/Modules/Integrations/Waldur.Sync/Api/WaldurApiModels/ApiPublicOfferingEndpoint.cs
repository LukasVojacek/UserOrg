using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

public sealed record ApiPublicOfferingEndpoint(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url);