using System.Text.Json;

namespace UserOrg.AppHost.Tests;

public sealed class VaultSetupCommandTests
{
    private const string CommandName = "vault-setup";

    [Fact]
    public void Should_RegisterOneVaultSetupCommand()
    {
        var command = ResourceCommandSyntax.Command(CommandName);

        Assert.NotEmpty(command.OptionsCode);
    }

    [Fact]
    public void Should_DeclareTheVaultSetupCommandOnTheVaultServerContainer()
    {
        var command = ResourceCommandSyntax.Command(CommandName);
        var vault = VaultSyntax.Call("AddContainer", "Vault");

        var onTheVaultChain = command.Registration.Receiver() == vault.AssignedVariable()
            || vault.Chain().Calls("WithCommand", CommandName).Count > 0;

        var throughAnExtension = command.DeclaringMethod is { } method
            && (vault.AppliesTo(method.Identifier.ValueText)
                || command.ReceiverTypes.Any(type => type.Contains("ContainerResource", StringComparison.Ordinal)));

        Assert.True(
            onTheVaultChain || throughAnExtension,
            "the vault-setup command is registered on something other than the Vault server container: "
                + command.Registration.Describe());
    }

    [Fact]
    public void Should_TakeTheModeAsAChoiceArgument()
    {
        var options = ResourceCommandSyntax.Command(CommandName).OptionsCode;

        Assert.Contains("Arguments", options, StringComparison.Ordinal);
        Assert.Contains("InputType.Choice", options, StringComparison.Ordinal);
        Assert.Contains("\"Mode\"", options, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Keep")]
    [InlineData("StructureOnly")]
    [InlineData("Mock")]
    public void Should_OfferTheThreeVaultSetupModes(string mode)
    {
        var command = ResourceCommandSyntax.Command(CommandName);
        var code = $"{command.OptionsCode}\n{command.ImplementationCode}";

        Assert.True(code.MentionsWord(mode), $"the vault-setup command does not know the `{mode}` mode");
    }

    [Fact]
    public void Should_DefaultToTheKeepMode()
    {
        var command = ResourceCommandSyntax.Command(CommandName);
        var options = command.OptionsCode;

        var preselected = options.Contains("Value = \"Keep\"", StringComparison.Ordinal);

        var keep = options.IndexOf("\"Keep\"", StringComparison.Ordinal);
        var structureOnly = options.IndexOf("\"StructureOnly\"", StringComparison.Ordinal);
        var mock = options.IndexOf("\"Mock\"", StringComparison.Ordinal);
        var listedFirst = keep >= 0 && keep < structureOnly && keep < mock;

        Assert.True(
            preselected || listedFirst,
            "Keep is not the default vault-setup mode, so clicking the highlighted toolbar command is not safe");
    }

    [Fact]
    public void Should_HighlightTheVaultSetupCommandInTheToolbar()
    {
        var options = ResourceCommandSyntax.Command(CommandName).OptionsCode;

        Assert.True(
            options.Contains("IsHighlighted = true", StringComparison.Ordinal),
            "the vault-setup command is not highlighted, so it stays in the overflow menu");
    }

    [Fact]
    public void Should_TalkToTheVaultHttpApiFromTheAppHostProcess()
    {
        Assert.Contains("HttpClient", VaultSyntax.Code, StringComparison.Ordinal);
        Assert.Contains("X-Vault-Token", VaultSyntax.Code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sys/mounts")]
    [InlineData("sys/auth/approle")]
    [InlineData("sys/policies/acl/userorg_pol")]
    [InlineData("auth/approle/role/userorg-role")]
    [InlineData("role-id")]
    [InlineData("secret-id")]
    [InlineData("example-path")]
    [InlineData("ExternalServiceAccessCredentials")]
    public void Should_ReachEveryVaultApiPathTheShellScriptsReached(string pathFragment)
    {
        Assert.True(
            VaultSyntax.Code.Contains(pathFragment, StringComparison.Ordinal),
            $"no Vault API path in the AppHost mentions `{pathFragment}`");
    }

    [Fact]
    public void Should_EnableTheUserorgKeyValueVersion2Mount()
    {
        var compact = VaultSyntax.Code.Compact();

        Assert.Contains("sys/mounts", VaultSyntax.Code, StringComparison.Ordinal);
        Assert.Contains("\"kv\"", compact, StringComparison.Ordinal);
        Assert.Contains("version", compact, StringComparison.Ordinal);
        Assert.Contains("\"2\"", compact, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("secret_id_ttl")]
    [InlineData("token_policies")]
    [InlineData("token_num_uses")]
    [InlineData("token_ttl")]
    [InlineData("token_max_ttl")]
    [InlineData("secret_id_num_uses")]
    public void Should_CreateTheAppRoleWithTheSameSettingsAsVaultInit(string setting)
    {
        Assert.True(
            VaultSyntax.Code.MentionsWord(setting),
            $"the vault-setup command does not set `{setting}` on the userorg-role AppRole");
    }

    [Fact]
    public void Should_WriteTheSameUserorgPolicyAsVaultInit()
    {
        var compact = VaultSyntax.Code.Compact();

        foreach (var fragment in new[]
        {
            "\"userorg/*\"",
            "capabilities=[\"create\",\"read\",\"update\",\"delete\",\"list\"]",
            "\"auth/token/create\"",
            "capabilities=[\"create\",\"read\",\"update\"]"
        })
        {
            Assert.True(
                compact.Contains(fragment, StringComparison.Ordinal),
                $"the userorg_pol policy the AppHost writes does not carry `{fragment}`");
        }
    }

    [Fact]
    public void Should_ReadTheAppRoleCredentialsOutOfTheVaultResponses()
    {
        Assert.Contains("role_id", VaultSyntax.Code, StringComparison.Ordinal);
        Assert.Contains("secret_id", VaultSyntax.Code, StringComparison.Ordinal);

        Assert.True(
            VaultSyntax.Code.Contains("Json", StringComparison.Ordinal),
            "nothing in the Vault composition parses the Vault responses that carry role_id and secret_id");
    }

    [Fact]
    public void Should_SeedTheMockDataFromSeedJson()
    {
        Assert.True(
            VaultSyntax.VaultVolumeFileExists("seed.json"),
            "seed.json is gone; the Mock mode has no data source");

        Assert.True(
            VaultSyntax.Code.Contains("seed.json", StringComparison.Ordinal),
            "the vault-setup command never reaches seed.json");
    }

    [Theory]
    [InlineData("heappe-mock")]
    [InlineData("CredentialType")]
    [InlineData("ApiKey")]
    [InlineData("AuthorizationHeader")]
    [InlineData("X-API-Key")]
    [InlineData("HeaderPrefix")]
    [InlineData("mock-key")]
    public void Should_SeedTheHeappeMockCredentialInMockMode(string fragment)
    {
        Assert.True(
            VaultSyntax.Code.Contains(fragment, StringComparison.Ordinal),
            $"the HEAppE mock credential the vault-setup command writes does not carry `{fragment}`");
    }

    [Fact]
    public void Should_SeedTheCyclopsMockCredentialInMockMode()
    {
        var payloads = VaultSyntax.Literals
            .Where(literal => literal.Contains("\"cyclops\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            payloads.Count == 1,
            $"expected exactly one AppHost string literal carrying the `cyclops` credential, found {payloads.Count}");

        using var payload = JsonDocument.Parse(payloads[0]);
        var cyclops = payload.RootElement.GetProperty("data").GetProperty("cyclops");

        Assert.Equal("cyclops", cyclops.GetProperty("Name").GetString());
        Assert.Equal("ApiKey", cyclops.GetProperty("CredentialType").GetString());
        Assert.False(string.IsNullOrEmpty(cyclops.GetProperty("AuthorizationHeader").GetString()));
        Assert.True(cyclops.TryGetProperty("HeaderPrefix", out _), "the cyclops credential has no HeaderPrefix");
        Assert.False(string.IsNullOrEmpty(cyclops.GetProperty("ApiKey").GetString()));

        var field = ResourceCommandSyntax.AllRoots.Values
            .SelectMany(root => root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            .Single(declarator => declarator.Initializer?.Value is LiteralExpressionSyntax literal
                && literal.Token.ValueText == payloads[0])
            .Identifier.ValueText;

        Assert.True(
            VaultSyntax.Calls("MergeSecretAsync").Any(call =>
                call.ArgumentList.Arguments
                    .Any(argument => argument.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == field)
                && call.ArgumentList.Arguments
                    .Any(argument => argument.Code().Contains("ExternalServiceAccessCredentials", StringComparison.Ordinal))),
            $"no MergeSecretAsync call writes `{field}` into Vault at `ExternalServiceAccessCredentials`");
    }

    [Fact]
    public void Should_DropTheVaultSeedContainer()
    {
        var dockerfiles = VaultSyntax.Calls("AddDockerfile")
            .Where(call => call.ArgumentList.Code().Contains("VaultSeed", StringComparison.Ordinal))
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            dockerfiles.Count == 0,
            $"the VaultSeed AddDockerfile resource is still registered:\n  {string.Join("\n  ", dockerfiles)}");

        Assert.DoesNotContain("vaultSeed.dockerfile", VaultSyntax.AppHostCode, StringComparison.Ordinal);

        var truncations = VaultSyntax.Calls("OnBeforeResourceStarted")
            .Where(call => call.Code().Contains("secret_id", StringComparison.Ordinal))
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            truncations.Count == 0,
            "the OnBeforeResourceStarted handler that truncates the AppRole credentials is still registered:\n  "
                + string.Join("\n  ", truncations));
    }

    [Theory]
    [InlineData("vault_init.sh")]
    [InlineData("vault_seed.sh")]
    [InlineData("vaultSeed.dockerfile")]
    public void Should_DeleteTheVaultSeedImageAndItsShellScripts(string fileName)
    {
        Assert.False(
            VaultSyntax.VaultVolumeFileExists(fileName),
            $"`{fileName}` still exists; the C# vault-setup command has not replaced it");
    }

    [Fact]
    public void Should_NotBindMountTheVaultVolumeFromTheRepositoryTree()
    {
        var mounts = VaultSyntax.Calls("WithBindMount")
            .Where(call => call.ArgumentList.Code().Contains("vaultVolume", StringComparison.Ordinal))
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            mounts.Count == 0,
            $"the AppHost still bind-mounts the repository's vaultVolume directory:\n  {string.Join("\n  ", mounts)}");
    }

    [Fact]
    public void Should_DeliverTheAgentConfigAsAContainerFile()
    {
        var containerFiles = VaultSyntax.Calls("WithContainerFiles")
            .Where(call => call.Code().Contains("vault-agent.hcl", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            containerFiles.Count > 0,
            "vault-agent.hcl is not delivered with WithContainerFiles");

        var mounts = VaultSyntax.Calls("WithBindMount")
            .Where(call => call.ArgumentList.Code().Contains("vault-agent.hcl", StringComparison.Ordinal))
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            mounts.Count == 0,
            $"vault-agent.hcl is still bind-mounted:\n  {string.Join("\n  ", mounts)}");
    }

    [Fact]
    public void Should_KeepTheAppRoleCredentialsOutsideTheRepositoryTree()
    {
        var machineLocalRoots = new[]
        {
            "GetFolderPath",
            "SpecialFolder",
            "GetTempPath",
            "GetEnvironmentVariable",
            "LOCALAPPDATA",
            "XDG_",
            "HOME"
        };

        Assert.True(
            machineLocalRoots.Any(root => VaultSyntax.Code.Contains(root, StringComparison.Ordinal)),
            "the AppRole credentials directory is not derived from a machine-local root; expected one of "
                + string.Join(", ", machineLocalRoots));
    }

    [Fact]
    public void Should_NotWriteTheAppRoleCredentialsIntoTheRepository()
    {
        var repositoryPaths = VaultSyntax.Literals
            .Where(literal => literal.Contains("role_id", StringComparison.Ordinal)
                || literal.Contains("secret_id", StringComparison.Ordinal))
            .Where(literal => literal.Contains("externalServices", StringComparison.Ordinal)
                || literal.Contains("vaultVolume", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            repositoryPaths.Count == 0,
            "the AppRole credentials are still addressed inside the repository tree:\n  "
                + string.Join("\n  ", repositoryPaths));

        var combines = VaultSyntax.Calls("Combine")
            .Where(call => call.ArgumentList.Code().Contains("AppHostDirectory", StringComparison.Ordinal))
            .Where(call => call.ArgumentList.Code().Contains("role_id", StringComparison.Ordinal)
                || call.ArgumentList.Code().Contains("secret_id", StringComparison.Ordinal)
                || call.ArgumentList.Code().Contains("vaultVolume", StringComparison.Ordinal))
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            combines.Count == 0,
            $"the AppHost still builds a vaultVolume path from AppHostDirectory:\n  {string.Join("\n  ", combines)}");
    }

    [Fact]
    public void Should_NotWaitForTheDeletedVaultSeedContainer()
    {
        var edges = VaultSyntax.Calls("WaitForCompletion")
            .Where(call => call.ArgumentList.Code().Contains("vaultInit", StringComparison.Ordinal)
                || call.ArgumentList.Code().Contains("VaultSeed", StringComparison.Ordinal))
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            edges.Count == 0,
            $"the VaultProxy still waits for the deleted VaultSeed container:\n  {string.Join("\n  ", edges)}");
    }

    [Fact]
    public void Should_StartTheVaultProxyOnlyAfterTheAppRoleCredentialsExist()
    {
        var proxy = VaultSyntax.Call("AddContainer", "VaultProxy");

        var startedByTheAppHostProcess = proxy.AppliesTo("WithExplicitStart")
            && VaultSyntax.Code.Contains("ResourceCommandService", StringComparison.Ordinal)
            && VaultSyntax.Code.Contains("ExecuteCommandAsync", StringComparison.Ordinal);

        var waitsForANewGate = proxy.AppliedCalls("WaitForCompletion")
            .Concat(proxy.AppliedCalls("WaitFor"))
            .Select(call => call.ArgumentList.Code())
            .Any(arguments => !arguments.MentionsWord("vaultInit")
                && !arguments.MentionsWord("vault")
                && !arguments.Contains("VaultSeed", StringComparison.Ordinal));

        Assert.True(
            startedByTheAppHostProcess || waitsForANewGate,
            "nothing keeps the VaultProxy from starting before the AppRole credentials are issued: it is neither explicit-start and started from the app host process through ResourceCommandService, nor held by a wait edge on a resource that issues the credentials");
    }

    [Fact]
    public void Should_RestartTheVaultProxyAfterReissuingTheCredentials()
    {
        Assert.Contains("ResourceCommandService", VaultSyntax.Code, StringComparison.Ordinal);

        Assert.True(
            VaultSyntax.Code.Contains("RestartCommand", StringComparison.Ordinal)
                || (VaultSyntax.Code.Contains("StopCommand", StringComparison.Ordinal)
                    && VaultSyntax.Code.Contains("StartCommand", StringComparison.Ordinal)),
            "the vault-setup command does not restart the VaultProxy after re-issuing the AppRole credentials");

        Assert.True(
            VaultSyntax.Code.MentionsWord("vaultProxy") || VaultSyntax.Code.Contains("\"VaultProxy\"", StringComparison.Ordinal),
            "the restart the vault-setup command performs does not name the VaultProxy resource");
    }
}
