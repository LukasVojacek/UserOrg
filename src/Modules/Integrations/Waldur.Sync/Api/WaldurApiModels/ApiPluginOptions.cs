using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

public sealed record ApiPluginOptions(
    [property: JsonPropertyName("heappe_local_base_path")] string? HeappeLocalBasePath);