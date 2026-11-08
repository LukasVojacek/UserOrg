using System.Text.Json.Serialization;

namespace Waldur.Sync.Api.WaldurApiModels;

/// <summary>Raw Waldur <c>/api/projects/{uuid}/list_users/</c> item.</summary>
public sealed record ApiProjectUser(
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("uuid")] Guid Id,
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("role_uuid")] Guid RoleId,
    [property: JsonPropertyName("role_name")] string RoleName,
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("user_uuid")] Guid UserId,
    [property: JsonPropertyName("user_username")] string UserName,
    [property: JsonPropertyName("user_email")] string? UserEmail,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonConverter(typeof(GuidNoDashJsonConverter))]
    [property: JsonPropertyName("created_by_uuid")] Guid? CreatedBy);