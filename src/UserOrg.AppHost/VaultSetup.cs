using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UserOrg.AppHost;

internal sealed class VaultSetup(
    IResourceBuilder<ContainerResource> vault,
    IResourceBuilder<ParameterResource> devRootToken,
    IResourceBuilder<ContainerResource> vaultProxy)
{
    public static string CredentialsDirectory { get; } = Path.Combine(Path.GetTempPath(), "userorg-vault");

    public static string AgentConfig { get; } = ReadEmbeddedText("vault-agent.hcl");

    private const string MountPayload = """{"type":"kv","options":{"version":"2"}}""";

    private const string AppRolePayload = """
        {
            "secret_id_ttl": "0",
            "token_policies": "userorg_pol",
            "token_num_uses": "0",
            "token_ttl": "0",
            "token_max_ttl": "0",
            "secret_id_num_uses": "0"
        }
        """;

    private const string HeappeMockCredentialPayload =
        """{"data":{"heappe-mock":{"Name":"heappe-mock","CredentialType":"ApiKey","AuthorizationHeader":"X-API-Key","HeaderPrefix":"","ApiKey":"mock-key"}}}""";

    private const string CyclopsMockCredentialPayload =
        """{"data":{"cyclops":{"Name":"cyclops","CredentialType":"ApiKey","AuthorizationHeader":"X-Api-Key","HeaderPrefix":"","ApiKey":"mock-cyclops-key"}}}""";

    private const string MergePatchContentType = "application/merge-patch+json";

    private const string UserorgPolicy = """
        path "userorg/*" {
            capabilities = ["create", "read", "update", "delete", "list"]
        }

        path "auth/token/create" {
            capabilities = ["create", "read", "update"]
        }
        """;

    public static void EnsureCredentialsDirectory()
    {
        Directory.CreateDirectory(CredentialsDirectory);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                CredentialsDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    public void ApplyTo(DistributedApplication app)
    {
        // `aspire publish` and `aspire deploy` build the model, ask every callback for its values and
        // exit; no orchestrator ever runs, so nothing publishes the resource states the starts below wait
        // for and a start would hang the publish forever.
        if (app.Services.GetRequiredService<DistributedApplicationExecutionContext>().IsPublishMode)
        {
            return;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<VaultSetup>();
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

        _ = Task.Run(() => IssueCredentialsAndStartProxyAsync(app, logger, lifetime.ApplicationStopping));
    }

    public async Task<ExecuteCommandResult> ExecuteAsync(ExecuteCommandContext context)
    {
        var cancellationToken = context.CancellationToken;
        var mode = context.Arguments["Mode"].Value ?? string.Empty;
        var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger<VaultSetup>();
        var commands = context.Services.GetRequiredService<ResourceCommandService>();
        var notifications = context.Services.GetRequiredService<ResourceNotificationService>();

        if (mode is not ("Keep" or "StructureOnly" or "Mock"))
        {
            return CommandResults.Failure($"Unknown vault-setup mode '{mode}'.");
        }

        try
        {
            await ApplyAsync(mode, logger, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return CommandResults.Failure(exception);
        }
        catch (HttpRequestException exception)
        {
            return CommandResults.Failure(exception);
        }
        catch (JsonException exception)
        {
            return CommandResults.Failure(exception);
        }
        catch (IOException exception)
        {
            return CommandResults.Failure(exception);
        }

        var restart = await RestartProxyAsync(commands, notifications, cancellationToken);

        if (!restart.Success)
        {
            return restart;
        }

        return CommandResults.Success(
            $"vault-setup: {mode} completed",
            $"Ran {mode} against the Vault development server and restarted {vaultProxy.Resource.Name}.",
            CommandResultFormat.Text);
    }

    private async Task IssueCredentialsAndStartProxyAsync(
        DistributedApplication app,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        var commands = app.Services.GetRequiredService<ResourceCommandService>();

        try
        {
            await WaitForVaultHealthyAsync(notifications, cancellationToken);

            await ApplyAsync("Mock", logger, cancellationToken, overwriteExistingData: false);

            await notifications.WaitForResourceAsync(
                vaultProxy.Resource.Name,
                KnownResourceStates.NotStarted,
                cancellationToken);

            var start = await commands.ExecuteCommandAsync(
                vaultProxy.Resource,
                KnownResourceCommands.StartCommand,
                cancellationToken);

            if (!start.Success)
            {
                logger.LogError(
                    "Could not start {Resource} after issuing the AppRole credentials: {Message}",
                    vaultProxy.Resource.Name,
                    start.Message);

                await ReportProxyFailedAsync(notifications, logger, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app host is shutting down. Nothing behind the proxy is going to run either way, and a
            // resource the orchestrator is already tearing down must not be painted red on the way out.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not set up the Vault development server; {Resource} stays stopped and the API gets no secrets.",
                vaultProxy.Resource.Name);

            // Including the bounded wait's timeout, which arrives here as an InvalidOperationException.
            await ReportProxyFailedAsync(notifications, logger, cancellationToken);
        }
    }

    /// <summary>
    /// Holds until the Vault development server reports healthy, or two minutes pass.
    /// </summary>
    /// <remarks>
    /// The unbounded wait this replaces had no terminal state of its own: a container that never reported
    /// healthy parked this task forever, so no AppRole credentials were issued, <c>VaultProxy</c> stayed in
    /// <c>NotStarted</c> and every <c>WaitFor(vaultProxy)</c> edge behind it waited on a resource nothing
    /// was ever going to start - a stuck dashboard with no failure on it. Two minutes is the deadline
    /// <see cref="DatabaseSourceSelection"/> gives the gateway's RBAC import for the same reason.
    /// </remarks>
    private async Task WaitForVaultHealthyAsync(
        ResourceNotificationService notifications,
        CancellationToken cancellationToken)
    {
        var name = vault.Resource.Name;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            await notifications.WaitForResourceHealthyAsync(name, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The outer token is the app host shutting down and is not a failure; only the deadline
            // expiring on its own is one.
            throw new InvalidOperationException(
                $"'{name}' did not report healthy within two minutes, so no AppRole credentials were issued and '{vaultProxy.Resource.Name}' was never started. Check the '{name}' container's logs and whether its /v1/sys/health endpoint answers.");
        }
    }

    /// <summary>
    /// Paints <c>VaultProxy</c> as failed, so a run that could not set Vault up ends somewhere visible.
    /// </summary>
    /// <remarks>
    /// The proxy is explicit-start and <see cref="IssueCredentialsAndStartProxyAsync"/> is the only thing
    /// that starts it, so every failure path there used to leave it in <c>NotStarted</c> - a state that
    /// reads as "waiting to be started", not "it never will be". <c>api.WaitFor(vaultProxy)</c> then
    /// hangs on it with nothing on screen but one log line.
    /// <c>FailedToStart</c> is terminal, so the dashboard shows the failure the waiters are stuck behind.
    /// </remarks>
    private async Task ReportProxyFailedAsync(
        ResourceNotificationService notifications,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var name = vaultProxy.Resource.Name;

        try
        {
            // Publishing before the orchestrator has registered the resource is a race the orchestrator
            // wins - its own NotStarted lands afterwards and paints over this, exactly the race
            // ReportSkippedAsync waits out. Here the wait has to be conditional: the failed-start path has
            // already been through NotStarted, and WaitForResourceAsync would then hold for a state the
            // resource is never going to re-enter. Any published state at all proves the registration
            // happened, so only a resource the orchestrator has said nothing about is waited for.
            if (!notifications.TryGetCurrentState(name, out var current) || current?.Snapshot.State is null)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));

                await notifications.WaitForResourceAsync(name, KnownResourceStates.NotStarted, deadline.Token);
            }

            await notifications.PublishUpdateAsync(vaultProxy.Resource, snapshot => snapshot with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error)
            });
        }
#pragma warning disable CA1031 // Reporting a failure must not replace the failure it is reporting.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                exception,
                "Could not publish a failed state for {Resource}; the dashboard still shows it as not started.",
                name);
        }
    }

    /// <param name="overwriteExistingData">
    /// The vault-setup command passes true: the developer asked for the mock data, so it is written even
    /// over secrets that are already there. Startup passes false, which seeds an empty Vault and leaves a
    /// Vault that already holds something alone - the container is Persistent, so its secrets outlive the
    /// run that created them and a developer's own entries must survive the next `aspire run`.
    /// </param>
    private async Task ApplyAsync(
        string mode,
        ILogger logger,
        CancellationToken cancellationToken,
        bool overwriteExistingData = true)
    {
        var address = VaultAddress;

        using var client = await CreateClientAsync(cancellationToken);

        if (mode is "StructureOnly" or "Mock")
        {
            await CreateStructureAsync(client, address, logger, cancellationToken);
        }

        await IssueCredentialsAsync(client, address, logger, cancellationToken);

        if (mode is "Mock")
        {
            var paths = await ListSecretPathsAsync(client, address, logger, cancellationToken);
            var empty = paths is { Count: 0 };

            if (overwriteExistingData || empty)
            {
                await SeedMockDataAsync(client, address, logger, cancellationToken);
            }
            else
            {
                logger.LogInformation(
                    "Vault already holds secrets under userorg/; leaving them untouched. Run the vault-setup command to write the mock data over them.");
            }
        }

        logger.LogInformation("Vault setup finished in {Mode} mode.", mode);
    }

    private string VaultAddress => vault.GetEndpoint("http").Url.TrimEnd('/');

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var token = await devRootToken.Resource.GetValueAsync(cancellationToken);

        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("X-Vault-Token", token);

        return client;
    }

    private static async Task CreateStructureAsync(
        HttpClient client,
        string address,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            client, HttpMethod.Post, $"{address}/v1/sys/mounts/userorg", MountPayload, logger, true, cancellationToken);

        await SendAsync(
            client, HttpMethod.Post, $"{address}/v1/sys/auth/approle", """{"type":"approle"}""", logger, true, cancellationToken);

        var policy = JsonSerializer.Serialize(new Dictionary<string, string> { ["policy"] = UserorgPolicy });

        await SendAsync(
            client, HttpMethod.Put, $"{address}/v1/sys/policies/acl/userorg_pol", policy, logger, false, cancellationToken);

        await SendAsync(
            client, HttpMethod.Post, $"{address}/v1/auth/approle/role/userorg-role", AppRolePayload, logger, false, cancellationToken);
    }

    private static async Task IssueCredentialsAsync(
        HttpClient client,
        string address,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var roleIdResponse = await SendAsync(
            client, HttpMethod.Get, $"{address}/v1/auth/approle/role/userorg-role/role-id", null, logger, false, cancellationToken);

        var secretIdResponse = await SendAsync(
            client, HttpMethod.Post, $"{address}/v1/auth/approle/role/userorg-role/secret-id", null, logger, false, cancellationToken);

        EnsureCredentialsDirectory();
        WriteCredential("role_id", DataField(roleIdResponse, "role_id"));
        WriteCredential("secret_id", DataField(secretIdResponse, "secret_id"));
    }

    /// <summary>
    /// The top-level paths under the userorg KV mount, or <see langword="null"/> when the mount could not
    /// be read. Vault answers a LIST over an empty or absent mount with 404, and with 200 plus a key list
    /// once anything has been written.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>?> ListSecretPathsAsync(
        HttpClient client,
        string address,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{address}/v1/userorg/metadata?list=true");
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            // Unreadable is not empty: seeding over secrets we could not inspect is the damage to avoid.
            logger.LogWarning(
                "Could not read the userorg secret store to decide whether to seed it ({Status}); treating it as non-empty and writing nothing.",
                response.StatusCode);

            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("keys", out var keys)
            ? [.. keys.EnumerateArray().Select(key => key.GetString()?.TrimEnd('/')).OfType<string>()]
            : [];
    }

    private static async Task SeedMockDataAsync(
        HttpClient client,
        string address,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var seed = ReadEmbeddedText("seed.json");

        await SendAsync(
            client,
            HttpMethod.Post,
            $"{address}/v1/userorg/data/example-path",
            $$"""{"data": {{seed}}}""",
            logger,
            false,
            cancellationToken);

        await MergeSecretAsync(
            client,
            $"{address}/v1/userorg/data/ExternalServiceAccessCredentials",
            HeappeMockCredentialPayload,
            logger,
            cancellationToken);

        await MergeSecretAsync(
            client,
            $"{address}/v1/userorg/data/ExternalServiceAccessCredentials",
            CyclopsMockCredentialPayload,
            logger,
            cancellationToken);
    }

    private static async Task MergeSecretAsync(
        HttpClient client,
        string url,
        string payload,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, MergePatchContentType),
        };

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new InvalidOperationException($"Vault answered {(int)response.StatusCode} to PATCH {url}: {body}");
        }

        logger.LogDebug("Vault carries no secret at {Url} yet; creating it instead of merging into it.", url);

        await SendAsync(client, HttpMethod.Post, url, payload, logger, false, cancellationToken);
    }

    private async Task<ExecuteCommandResult> RestartProxyAsync(
        ResourceCommandService commands,
        ResourceNotificationService notifications,
        CancellationToken cancellationToken)
    {
        var name = vaultProxy.Resource.Name;
        notifications.TryGetCurrentState(name, out var current);
        var state = current?.Snapshot.State?.Text;

        var command = string.IsNullOrEmpty(state)
            || state == KnownResourceStates.NotStarted
            || KnownResourceStates.TerminalStates.Contains(state)
                ? KnownResourceCommands.StartCommand
                : KnownResourceCommands.RestartCommand;

        var result = await commands.ExecuteCommandAsync(vaultProxy.Resource, command, cancellationToken);

        if (!result.Success)
        {
            return CommandResults.Failure($"Could not restart '{name}': {result.Message}");
        }

        // Bounded since VaultProxy gained a health check: the probe is an authenticated Vault path, so a
        // proxy that never completes AppRole auto-auth never reports healthy and this command would spin
        // until the developer cancelled it. The deadline turns that into an answer on the dashboard.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            await notifications.WaitForResourceHealthyAsync(name, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return CommandResults.Failure(
                $"'{name}' started but did not report healthy within two minutes. Its health probe is an authenticated Vault path, so this usually means AppRole auto-auth never completed; check the '{name}' container's logs and the credentials under {CredentialsDirectory}.");
        }

        return CommandResults.Success();
    }

    private static async Task<string> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? payload,
        ILogger logger,
        bool tolerateExisting,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);

        if (payload is not null)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        if (tolerateExisting && response.StatusCode == HttpStatusCode.BadRequest)
        {
            logger.LogDebug("Vault already carries what {Url} would create: {Body}", url, body);

            return body;
        }

        throw new InvalidOperationException($"Vault answered {(int)response.StatusCode} to {method} {url}: {body}");
    }

    private static string DataField(string response, string field)
    {
        using var document = JsonDocument.Parse(response);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty(field, out var value)
            || value.GetString() is not { Length: > 0 } text)
        {
            throw new InvalidOperationException($"The Vault response carries no '{field}': {response}");
        }

        return text;
    }

    private static void WriteCredential(string name, string value)
    {
        var path = Path.Combine(CredentialsDirectory, name);
        File.WriteAllText(path, value);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    internal static string ReadEmbeddedText(string name)
    {
        using var stream = typeof(VaultSetup).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The app host assembly does not carry the embedded resource '{name}'.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

internal static class VaultSetupExtensions
{
    public static IResourceBuilder<ContainerResource> WithVaultSetupCommand(
        this IResourceBuilder<ContainerResource> vault,
        VaultSetup setup) =>
        vault.WithCommand(
            name: "vault-setup",
            displayName: "Vault setup",
            commandOptions: new CommandOptions
            {
                Description = "Prepares the Vault development server: the userorg KV v2 mount, the AppRole and its policy, the AppRole credentials, and the mock data.",
                Arguments =
                [
                    new InteractionInput
                    {
                        Name = "Mode",
                        Label = "Mode",
                        InputType = InputType.Choice,
                        Required = true,
                        Value = "Keep",
                        Options =
                        [
                            new("Keep", "Keep the data, re-issue the AppRole credentials"),
                            new("StructureOnly", "Mount, AppRole and policy, no data"),
                            new("Mock", "Structure and the mock data"),
                        ]
                    }
                ],
                UpdateState = context => context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
                    ? ResourceCommandState.Enabled
                    : ResourceCommandState.Disabled,
                IconName = "ShieldKeyhole",
                IconVariant = IconVariant.Filled,
                IsHighlighted = true
            },
            executeCommand: setup.ExecuteAsync);
}