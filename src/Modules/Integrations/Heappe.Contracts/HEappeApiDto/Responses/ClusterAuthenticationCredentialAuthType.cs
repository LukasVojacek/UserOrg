namespace Heappe.Contracts.HEappeApiDto.Responses;

/// <summary>
/// Represents a cluster authentication credential authentication type from HEAppE API.
/// </summary>
public record ClusterAuthenticationCredentialAuthType
{
    /// <summary>
    /// Gets the unique identifier for the authentication type.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the name of the authentication type.
    /// Examples: "Password", "PrivateKey", "SshCertificate", "Kerberos", etc.
    /// </summary>
    public string Name { get; init; } = string.Empty;
}