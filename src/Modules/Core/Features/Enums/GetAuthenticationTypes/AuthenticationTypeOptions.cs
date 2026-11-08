namespace Core.Features.Enums.GetAuthenticationTypes;

/// <summary>
/// The cluster authentication type allowlist, bound to the <c>Heappe:ClusterAuthTypes</c> section the HEAppE
/// integration also reads. Core binds it itself because the integration's options type is internal to that module.
/// </summary>
internal sealed class AuthenticationTypeOptions
{
    public const string ConfigurationPath = "Heappe:ClusterAuthTypes";

    public List<string> AllowedAuthenticationTypeNames { get; set; } = [];
}