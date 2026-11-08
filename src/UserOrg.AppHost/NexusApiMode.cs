using System.Text.Json;

namespace UserOrg.AppHost;

// Aspire-free on purpose: UserOrg.AppHost.Tests compiles this file through <Compile Link>, so it may reference
// nothing outside the BCL.

/// <summary>How the <c>nexusapi</c> service name that Core API resolves is backed.</summary>
internal enum NexusApiMode
{
    /// <summary>The NexusApi image runs as a container beside the app host (with its own Postgres).</summary>
    Container,

    /// <summary>A NexusApi somewhere else, reached at the <c>NexusApiBaseUrl</c> parameter.</summary>
    External,
}

internal static class NexusApiModes
{
    internal const string DefaultServiceAccountClient = "userorg";

    internal const string HeappeMockInstance = "heappe-mock";

    private const string HeappeMockClientId = "heappe-mock-client";

    private static readonly string[] RegistryRead = ["registry:read"];

    private static readonly string[] KeycloakScopes = ["openid"];

    /// <summary>
    /// The mode a run already carries in <c>Parameters:NexusApiSource</c>, or <see langword="null"/> when it carries
    /// none and the dashboard dialog has to ask.
    /// </summary>
    public static NexusApiMode? Parse(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (!Enum.TryParse<NexusApiMode>(configured, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                $"Parameters:NexusApiSource is '{configured}'; expected '{NexusApiMode.Container}' or '{NexusApiMode.External}'.");
        }

        return mode;
    }

    /// <summary>
    /// The value of <c>FeatureTokenExchangeEnabled</c>: off when blank, otherwise a plain boolean.
    /// </summary>
    public static bool ParseTokenExchangeEnabled(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        return bool.TryParse(configured.Trim(), out var enabled)
            ? enabled
            : throw new InvalidOperationException(
                $"Parameters:FeatureTokenExchangeEnabled is '{configured}'; expected 'true' or 'false'.");
    }

    /// <summary>
    /// The file the <c>import</c> command loads into the container's registry: the service account named
    /// <paramref name="serviceAccountClient"/> (registry read), the Keycloak client that account may use for the HEAppE mock, and the instance that routes
    /// <c>/api/heappe/heappe-mock/</c> to <paramref name="upstreamUrl"/>. No instance when there is no upstream.
    /// </summary>
    public static string ImportDocument(string serviceAccountClient, string serviceAccountSecret, string? upstreamUrl) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            serviceAccounts = new[]
            {
                new
                {
                    name = serviceAccountClient,
                    clientId = serviceAccountClient,
                    keycloakClientId = serviceAccountClient,
                    secret = serviceAccountSecret,
                    permissions = RegistryRead,
                },
            },
            keycloakClients = new[]
            {
                new
                {
                    name = HeappeMockInstance,
                    clientId = HeappeMockClientId,
                    scopes = KeycloakScopes,
                    allowedServices = new[] { serviceAccountClient },
                },
            },
            proxyInstances = string.IsNullOrWhiteSpace(upstreamUrl)
                ? Array.Empty<object>()
                : new object[] { new { type = "heappe", name = HeappeMockInstance, url = upstreamUrl, enabled = true } },
        });
}