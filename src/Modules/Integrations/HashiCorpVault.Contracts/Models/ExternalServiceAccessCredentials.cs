using System.Diagnostics;
using System.Text.Json.Serialization;

namespace HashiCorpVault.Contracts.Models;

[DebuggerDisplay("{Name}")]
public class ExternalServiceAccessCredentials : IVaultModel
{
    public static string VaultPath { get; } = "ExternalServiceAccessCredentials";

    public required string Name { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ECredentialType CredentialType { get; init; }
    /// <summary>
    /// Gets the authorization header value used for authenticating requests, such as an 'Authorization' or
    /// 'X-Api-Token' token.
    /// </summary>
    /// <remarks>This property is required and must be set during initialization to ensure proper
    /// authentication when accessing protected resources.</remarks>
    public required string AuthorizationHeader { get; init; }
    /// <summary>
    /// Gets the prefix used in the authorization header, such as "Bearer" or "Basic".
    /// </summary>
    /// <remarks>This property is required and must be initialized with a valid string value representing the
    /// authorization scheme.</remarks>
    public required string HeaderPrefix { get; init; }
    public string? ApiKey { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }
}