using System.Text.Json.Serialization;

namespace Forge.Core.Features.Role;

[JsonSerializable(typeof(RoleImportDto))]
[JsonSerializable(typeof(RoleItemDto))]
internal partial class RoleImportJsonContext : JsonSerializerContext
{
}