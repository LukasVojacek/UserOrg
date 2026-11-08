using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

/// <summary>Raw Waldur <c>/api/marketplace-public-offerings/?customer_uuid=…</c> item.</summary>
public sealed record ApiPublicOffering(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("endpoints")] IReadOnlyList<ApiPublicOfferingEndpoint>? Endpoints,
    [property: JsonPropertyName("components")] IReadOnlyList<ApiPublicOfferingComponent>? Components,
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("customer_uuid")] Guid CustomerId,
    [property: JsonPropertyName("created")] DateTimeOffset? Created,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("category_title")] string Type,
    [property: JsonPropertyName("shared")] bool IsShared,
    [property: JsonPropertyName("plugin_options")] ApiPluginOptions? PluginOptions);