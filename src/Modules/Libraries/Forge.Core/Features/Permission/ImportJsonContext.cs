using System.Text.Json.Serialization;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Forge.Core.Tests")]

namespace Forge.Core.Features.Permission;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PermissionImportDto))]
internal partial class ImportJsonContext : JsonSerializerContext { }