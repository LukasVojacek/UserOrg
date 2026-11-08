using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The environment and service overrides that let the real <c>Program</c> boot without external infrastructure.
/// </summary>
internal static class ProgramBootEnvironment
{
    public const string DefaultGatewayAuthority = "https://gateway-boot-test.invalid";
    public const string DefaultGatewayAudience = "gateway-internal";

    /// <summary>
    /// The boot keys and their values. <paramref name="directoryStem"/> names the spool and key cache directories,
    /// <paramref name="immuDbCredential"/> the ImmuDb user, password and database.
    /// </summary>
    public static KeyValuePair<string, string?>[] Values(
        string directoryStem = "audit-boot-tests",
        string immuDbCredential = "boot-tests",
        string gatewayAuthority = DefaultGatewayAuthority) =>
    [
        new("VaultCredentials__Address", "http://localhost:1"),
        new("VaultCredentials__MountPoint", "test"),
        new("AuthenticationConfiguration__MetadataAddress", "https://openapi-build.invalid"),
        new("MailConfiguration__MailSenderUrl", "https://openapi-build.invalid"),
        new("Heappe__ClusterAuthTypes__AllowedAuthenticationTypeNames__0", "SshCertificate"),
        new("EncryptedSpool__SpoolDirectory", Path.Combine(Path.GetTempPath(), $"userorg-{directoryStem}-spool")),
        new("EncryptedSpool__EndpointBaseUrl", "https://openapi-build.invalid"),
        new("OghamSealing__KeyEndpoint", "https://openapi-build.invalid/.well-known/ogham-sealing-keys.json"),
        new("OghamSealing__CacheDirectory", Path.Combine(Path.GetTempPath(), $"userorg-{directoryStem}-keys")),
        new("ImmuDb__Username", immuDbCredential),
        new("ImmuDb__Password", immuDbCredential),
        new("ImmuDb__Database", immuDbCredential),
        new("GatewayInternal__Authority", gatewayAuthority),
        new("GatewayInternal__Audience", DefaultGatewayAudience),
        new(NexusBaseUrlKey, DefaultNexusBaseUrl),
    ];

    /// <summary>
    /// The one NexusApi setting every boot needs (<c>TokenExchange:Nexus:BaseUrl</c>). The boots run with the token
    /// exchange flag off, so the NexusApi service account (<c>TokenExchange__Nexus__ClientId</c>,
    /// <c>TokenExchange__Nexus__ClientSecret</c>) is deliberately not set: every boot is also the QS-08 case.
    /// </summary>
    public const string NexusBaseUrlKey = "TokenExchange__Nexus__BaseUrl";

    public const string DefaultNexusBaseUrl = "https://nexusapi-boot-test.invalid";

    /// <summary>Sets every key of <see cref="Values"/> that the process environment leaves empty.</summary>
    public static void Apply()
    {
        foreach (var (key, value) in Values())
        {
            SetIfAbsent(key, value!);
        }
    }

    private static void SetIfAbsent(string key, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>Removes every hosted service, so no background worker reaches for infrastructure.</summary>
    public static void RemoveHostedServices(IServiceCollection services)
    {
        foreach (var hostedService in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
        {
            services.Remove(hostedService);
        }
    }
}
