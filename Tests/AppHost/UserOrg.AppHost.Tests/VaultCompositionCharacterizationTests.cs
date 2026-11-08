using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;

namespace UserOrg.AppHost.Tests;

public sealed class VaultCompositionCharacterizationTests
{
    [Fact]
    public void Should_KeepTheVaultDevRootTokenParameter()
    {
        var parameter = VaultSyntax.Call("AddParameter", "VaultDevRootToken");
        var code = parameter.Chain().Code();

        Assert.True(
            Regex.IsMatch(code, @"secret\s*:\s*true"),
            $"the VaultDevRootToken parameter is no longer declared as a secret: {parameter.Describe()}");

        Assert.True(
            parameter.AppliesTo("WithCustomInput"),
            "the VaultDevRootToken parameter lost its custom dashboard input");

        Assert.Contains("InputType.SecretText", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheVaultServerContainer()
    {
        var vault = VaultSyntax.Call("AddContainer", "Vault");

        Assert.Contains("hashicorp/vault", vault.Chain().Code(), StringComparison.Ordinal);

        foreach (var method in new[]
        {
            "WithLifetime",
            "WithEnvironment",
            "WithContainerRuntimeArgs",
            "WithHttpEndpoint",
            "WithExternalHttpEndpoints"
        })
        {
            Assert.True(vault.AppliesTo(method), $"the Vault container lost {method}");
        }

        foreach (var token in new[] { "VAULT_DEV_ROOT_TOKEN_ID", "IPC_LOCK", "8200", "ContainerLifetime.Persistent" })
        {
            Assert.Contains(token, VaultSyntax.Code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Should_KeepTheVaultServerHealthCheck()
    {
        var vault = VaultSyntax.Call("AddContainer", "Vault");

        Assert.True(vault.AppliesTo("WithHttpHealthCheck"), "the Vault container lost its health check");
        Assert.Contains("/v1/sys/health", VaultSyntax.Code, StringComparison.Ordinal);
        Assert.Contains("VAULT_ADDR", VaultSyntax.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheVaultProxyContainer()
    {
        var proxy = VaultSyntax.Call("AddContainer", "VaultProxy");

        Assert.Contains("hashicorp/vault", proxy.Chain().Code(), StringComparison.Ordinal);

        foreach (var method in new[]
        {
            "WithArgs",
            "WithContainerRuntimeArgs",
            "WithHttpEndpoint",
            "WithExternalHttpEndpoints",
            "WithUrlForEndpoint",
            "WithLifetime"
        })
        {
            Assert.True(proxy.AppliesTo(method), $"the VaultProxy container lost {method}");
        }

        foreach (var token in new[] { "proxy", "-config=/vault/config/vault-agent.hcl", "IPC_LOCK", "8100" })
        {
            Assert.Contains(token, VaultSyntax.Code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// F3: <c>WaitFor(vaultProxy)</c> releases on <em>Running</em>, and Running is not ready. The Vault
    /// agent proxy serves its listener before auto-auth has even started, so without a health check the API
    /// and the audit proxy are released while the agent still holds no AppRole token.
    /// </summary>
    /// <remarks>
    /// The probe has to be an <em>authenticated</em> Vault path. <c>use_auto_auth_token = "force"</c> does
    /// not hold a request back - it strips the caller's own token and then reads the auto-auth sink without
    /// blocking - so every unauthenticated endpoint, <c>/v1/sys/health</c> among them, answers 200 through a
    /// proxy that has no token at all. That is a health check that is green exactly when the defect is
    /// present.
    /// </remarks>
    [Fact]
    public void Should_GateTheVaultProxyOnAHealthCheckThatProvesAppRoleAuthCompleted()
    {
        var proxy = VaultSyntax.Call("AddContainer", "VaultProxy");
        var checks = proxy.AppliedCalls("WithHttpHealthCheck");

        Assert.True(
            checks.Count > 0,
            "the VaultProxy container carries no HTTP health check, so `api.WaitFor(vaultProxy)` releases the moment the container reports Running - which is before the Vault agent has completed AppRole auto-auth. The API reads Vault eagerly at host build and VaultConfigurationProvider.Load() swallows every failure, so the API then boots with no Vault secrets at all and nothing says so.");

        var probes = string.Join('\n', checks.Select(check => check.ArgumentList.Code()));

        Assert.True(
            probes.Contains("/v1/", StringComparison.Ordinal),
            $"the VaultProxy health check does not probe a Vault API path (`/v1/...`); `/health` is the audit proxy's own endpoint and the Vault agent proxy does not serve it, so the check could only ever be red: {probes}");

        Assert.False(
            probes.Contains("/v1/sys/health", StringComparison.Ordinal),
            $"the VaultProxy health check probes /v1/sys/health, which is an unauthenticated Vault endpoint. `use_auto_auth_token = \"force\"` does not block un-authenticated forwarding - Vault's ProxyHandler reads the auto-auth sink without waiting and forwards with an empty token - so the proxy answers 200 there before AppRole auth has happened, and WaitFor(vaultProxy) still releases too early. Probe a path that needs the token, such as /v1/auth/token/lookup-self: {probes}");
    }

    /// <summary>
    /// F2, first defect: the wait for the Vault server to report healthy carried only the app host's
    /// shutdown token, so a container that never reported healthy parked the startup task forever - no
    /// AppRole credentials, VaultProxy stuck in NotStarted, and everything behind it waiting on a resource
    /// nothing was going to start.
    /// </summary>
    [Fact]
    public void Should_BoundTheWaitForTheVaultServerToReportHealthy()
    {
        var waiter = StartupPathMethods()
            .Where(method => method.Code().Contains("WaitForResourceHealthyAsync", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            waiter.Count == 1,
            $"expected exactly one method on VaultSetup's startup path to wait for the Vault server to report healthy, found {waiter.Count}");

        var code = waiter[0].Code();

        Assert.True(
            code.MentionsWord("CancelAfter"),
            $"`{waiter[0].Identifier.ValueText}` waits for the Vault server to report healthy with no deadline: a Vault container that never turns healthy holds this wait forever, the AppRole credentials are never issued and VaultProxy is never started. Bound it the way DatabaseSourceSelection.WaitForRbacRolesAsync does, with a linked CancellationTokenSource and CancelAfter.");

        Assert.True(
            code.MentionsWord("CreateLinkedTokenSource"),
            $"`{waiter[0].Identifier.ValueText}` has a deadline that is not linked to the app host's own cancellation token, so a shutdown no longer cancels the wait");

        Assert.True(
            code.MentionsWord("InvalidOperationException"),
            $"`{waiter[0].Identifier.ValueText}` lets the deadline expire as a plain cancellation, which is indistinguishable from the app host shutting down; the expiry has to surface as an InvalidOperationException that names the resource and what to check");
    }

    /// <summary>
    /// F2, second defect: every failure path left VaultProxy in NotStarted forever. It is explicit-start
    /// and IssueCredentialsAndStartProxyAsync is the only thing that starts it, so `WaitFor(vaultProxy)`
    /// hung on a resource with no terminal state and one log line to explain it.
    /// </summary>
    [Fact]
    public void Should_PublishATerminalStateForTheVaultProxyOnEveryFailurePath()
    {
        var methods = StartupPathMethods();
        var entry = methods.Single(method => method.Identifier.ValueText == StartupMethodName);

        Assert.True(
            string.Join('\n', methods.Select(method => method.Code()))
                .Contains("KnownResourceStates.FailedToStart", StringComparison.Ordinal),
            $"nothing on VaultSetup's startup path publishes KnownResourceStates.FailedToStart. VaultProxy is explicit-start and {StartupMethodName} is the only thing that starts it, so a failure there leaves it in NotStarted forever - a dashboard with no terminal state on it and the WaitFor(vaultProxy) edge hanging behind a resource that will never run.");

        var failedStart = entry.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .SingleOrDefault(statement => statement.Condition.Code().Contains("start.Success", StringComparison.Ordinal));

        Assert.True(failedStart is not null, $"`{StartupMethodName}` no longer checks whether the start command succeeded");

        Assert.True(
            PublishesTheTerminalState(failedStart!, methods),
            "the failed start-command branch only logs; VaultProxy stays in NotStarted and every WaitFor(vaultProxy) edge waits on a resource that will never run. Publish KnownResourceStates.FailedToStart for it.");

        var generic = CatchOf(entry, "Exception");

        Assert.True(
            PublishesTheTerminalState(generic, methods),
            $"the generic catch in {StartupMethodName} only logs. Both the bounded wait's timeout and any other failure land here, and VaultProxy - which nothing else starts - is then left in NotStarted forever. Publish KnownResourceStates.FailedToStart for it.");

        var shutdown = CatchOf(entry, "OperationCanceledException");
        var shutdownCode = shutdown.Code();

        Assert.False(
            shutdownCode.Contains("FailedToStart", StringComparison.Ordinal),
            "the OperationCanceledException branch is the app host shutting down, not a failure; painting VaultProxy red on the way out reports a defect that is not there");
    }

    private const string StartupMethodName = "IssueCredentialsAndStartProxyAsync";

    /// <summary>
    /// <see cref="StartupMethodName"/> and the VaultSetup methods it calls - the code the app host runs
    /// after Build() to issue the AppRole credentials and start the proxy.
    /// </summary>
    private static IReadOnlyList<MethodDeclarationSyntax> StartupPathMethods()
    {
        var methods = AppHostSource.RootOf("VaultSetup.cs")
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToList();

        var entry = methods.SingleOrDefault(method => method.Identifier.ValueText == StartupMethodName);

        Assert.True(entry is not null, $"VaultSetup no longer declares `{StartupMethodName}`");

        var called = entry!.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(AppHostSyntax.NameOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return [.. methods.Where(method => method == entry || called.Contains(method.Identifier.ValueText))];
    }

    private static CatchClauseSyntax CatchOf(MethodDeclarationSyntax method, string exceptionType)
    {
        var matches = method.DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .Where(clause => clause.Declaration?.Type.ToString() == exceptionType)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"expected exactly one `catch ({exceptionType})` in `{method.Identifier.ValueText}`, found {matches.Count}");

        return matches[0];
    }

    /// <summary>
    /// Whether a failure branch publishes the terminal state itself, or through a VaultSetup method it
    /// calls - the helper shape is allowed, an unreported failure is not.
    /// </summary>
    private static bool PublishesTheTerminalState(SyntaxNode branch, IReadOnlyList<MethodDeclarationSyntax> methods)
    {
        if (branch.Code().Contains("FailedToStart", StringComparison.Ordinal))
        {
            return true;
        }

        var called = branch.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(AppHostSyntax.NameOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return methods.Any(method => called.Contains(method.Identifier.ValueText)
            && method.Code().Contains("FailedToStart", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_KeepTheApiWaitingForTheVaultProxy()
    {
        var program = AppHostSource.Program.Code();

        Assert.True(
            Regex.IsMatch(program, @"WaitFor\s*\(\s*vaultProxy\s*\)"),
            "the API no longer waits for the VaultProxy; the cold-start guarantee's downstream edge is gone");

        Assert.True(
            Regex.IsMatch(program, @"vaultProxy\s*\.\s*GetEndpoint\s*\(\s*""http""\s*\)"),
            "the API no longer reads its Vault address from the VaultProxy endpoint");
    }

    [Fact]
    public void Should_KeepTheAgentAutoAuthConfiguration()
    {
        var agentConfig = VaultSyntax.VaultVolumeFile("vault-agent.hcl");

        foreach (var fragment in new[]
        {
            "auto_auth",
            "type = \"approle\"",
            "remove_secret_id_file_after_reading = false",
            "use_auto_auth_token = \"force\"",
            "0.0.0.0:8100",
            "http://vault:8200"
        })
        {
            Assert.Contains(fragment, agentConfig, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Should_DeliverTheAppRoleCredentialsWhereTheAgentConfigReadsThem()
    {
        var deliveries = VaultSyntax.Calls("WithBindMount")
            .Concat(VaultSyntax.Calls("WithContainerFiles"))
            .Select(call => call.ArgumentList.Code())
            .ToList();

        foreach (var key in new[] { "role_id_file_path", "secret_id_file_path" })
        {
            var containerPath = VaultSyntax.AgentConfigValue(key);

            Assert.False(
                string.IsNullOrEmpty(containerPath),
                $"vault-agent.hcl no longer configures {key}");

            var containerDirectory = containerPath[..containerPath.LastIndexOf('/')];

            Assert.True(
                deliveries.Any(delivery =>
                    delivery.Contains(containerPath, StringComparison.Ordinal)
                    || delivery.Contains(containerDirectory, StringComparison.Ordinal)),
                $"nothing the AppHost mounts or copies into the VaultProxy lands on `{containerPath}`, which vault-agent.hcl reads as {key}");
        }

        foreach (var fileName in new[] { "role_id", "secret_id" })
        {
            Assert.Contains(fileName, VaultSyntax.Code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Should_KeepTheSeedDataForTheMockMode()
    {
        using var seed = JsonDocument.Parse(VaultSyntax.VaultVolumeFile("seed.json"));
        var root = seed.RootElement;

        Assert.Equal("example_value_1", root.GetProperty("example_key_1").GetString());
        Assert.Equal("example_value_2", root.GetProperty("example_key_2").GetString());

        var nested = root.GetProperty("nested_data");
        Assert.Equal("sub_value_1", nested.GetProperty("sub_key_1").GetString());
        Assert.Equal("sub_value_2", nested.GetProperty("sub_key_2").GetString());
    }

    [Fact]
    public void Should_KeepTheHeappeMockCredentialContractTheApiReadsFromVault()
    {
        var vaultPath = Path.Combine(
            AppHostSource.RepositoryRoot,
            "src",
            "Modules",
            "Integrations",
            "HashiCorpVault.Contracts",
            "Models",
            "ExternalServiceAccessCredentials.cs");

        Assert.True(File.Exists(vaultPath), $"expected `{vaultPath}` to exist");
        var contract = File.ReadAllText(vaultPath);

        Assert.Contains("\"ExternalServiceAccessCredentials\"", contract, StringComparison.Ordinal);

        foreach (var member in new[] { "Name", "CredentialType", "AuthorizationHeader", "HeaderPrefix", "ApiKey" })
        {
            Assert.Contains(member, contract, StringComparison.Ordinal);
        }
    }
}
