using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using UserOrg.AppHost;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// NexusApi in the AppHost (#349, decisions Q1 and Q2): under <c>aspire run</c> it is a container resource with its
/// own Postgres, a one-shot <c>migrate</c> and <c>import</c> of a generated config file, then the server with
/// <c>RUN_MIGRATIONS=false</c>; or an external NexusApi at the base URL parameter. The mode is a
/// dashboard dialog answered after <c>Build()</c> (<c>Parameters:NexusApiSource</c> skips it). The mode parsing and the import
/// document are plain code (<c>NexusApiMode.cs</c>, linked in); the resource wiring is asserted on its syntax, as
/// every other AppHost test here (see the csproj). T-040: <c>TokenExchange__Nexus__BaseUrl</c> is always set, the
/// NexusApi service account and the exchange client only when <c>FeatureTokenExchangeEnabled</c> is on, read in the
/// resource-start callbacks.
/// </summary>
public sealed partial class NexusApiResourceTests
{
    private const string ExtensionsFile = "NexusApiExtensions.cs";

    [Fact]
    public void Parse_asks_the_dialog_when_the_run_carries_no_answer()
    {
        Assert.Null(NexusApiModes.Parse(null));
        Assert.Null(NexusApiModes.Parse("  "));
    }

    [Theory]
    [InlineData("Container", "Container")]
    [InlineData("external", "External")]
    public void Parse_reads_the_configured_mode(string configured, string expected) =>
        Assert.Equal(expected, NexusApiModes.Parse(configured).ToString());

    [Theory]
    [InlineData("Mock")]
    [InlineData("Docker")]
    [InlineData("7")]
    public void Parse_rejects_an_unknown_mode(string configured) =>
        Assert.Throws<InvalidOperationException>(() => NexusApiModes.Parse(configured));

    [Fact]
    public void Mode_is_a_dialog_answered_after_build_like_the_database_source()
    {
        var prompt = AppHostSource.TextOf("RunSourcesDialog.cs");
        Assert.Contains("PromptInputsAsync", prompt, StringComparison.Ordinal);
        Assert.Contains("InputType.Choice", prompt, StringComparison.Ordinal);
        Assert.Contains("IsAvailable", prompt, StringComparison.Ordinal);
        Assert.Contains("Parameters:NexusApiSource", prompt, StringComparison.Ordinal);

        var program = AppHostSource.ProgramText;
        Assert.True(
            program.IndexOf("builder.Build()", StringComparison.Ordinal) < program.IndexOf("nexusApi.ApplyTo(", StringComparison.Ordinal),
            "the NexusApi selection is applied after Build(), when the dashboard can answer it");
    }

    [Fact]
    public void External_mode_reads_user_secrets_and_asks_only_for_what_is_missing()
    {
        var selection = AppHostSource.TextOf("NexusApiSelection.cs");
        var prompt = AppHostSource.TextOf("NexusApiPrompt.cs");

        Assert.Contains("Parameters:NexusApiBaseUrl", selection, StringComparison.Ordinal);
        Assert.Contains("Parameters:NexusApiServiceAccountClient", selection, StringComparison.Ordinal);
        Assert.Contains("Parameters:NexusApiServiceAccountSecret", selection, StringComparison.Ordinal);
        Assert.Contains("AskExternalAsync", selection, StringComparison.Ordinal);
        Assert.Contains("PromptInputsAsync", prompt, StringComparison.Ordinal);
        Assert.Contains("TrySetSecret", prompt, StringComparison.Ordinal);
        Assert.Contains("SaveToUserSecrets", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Container_secrets_are_saved_only_when_the_container_is_chosen()
    {
        var extensions = AppHostSource.TextOf(ExtensionsFile);

        Assert.DoesNotContain("persist: true", extensions, StringComparison.Ordinal);
        Assert.Contains("SaveWhenContainerChosen", extensions, StringComparison.Ordinal);
        Assert.Contains("_containerSecrets", AppHostSource.TextOf("NexusApiSelection.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AddNexusBaseUrl")]
    [InlineData("AddExternalServiceAccountSecret")]
    public void External_url_and_secret_are_parameters_only_in_publish_mode_so_a_container_run_is_not_asked(string call)
    {
        var calls = AppHostSource.RootOf(ExtensionsFile).Calls(call);

        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.Contains(
            c.Ancestors().OfType<IfStatementSyntax>(),
            branch => branch.Condition.ToString() == "!builder.ExecutionContext.IsRunMode"));
    }

    [Theory]
    [InlineData("AddPostgres")]
    [InlineData("AddContainer")]
    public void Container_chain_is_explicit_start_so_the_other_modes_keep_it_stopped(string call)
    {
        var calls = AppHostSource.RootOf(ExtensionsFile).Calls(call);

        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.Single(c.AppliedCalls("WithExplicitStart")));
    }

    [Fact]
    public void ImportDocument_creates_the_userorg_service_account_with_registry_read()
    {
        using var document = JsonDocument.Parse(NexusApiModes.ImportDocument("userorg", "account-secret", "http://heappe-mock:80"));

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var accounts = document.RootElement.GetProperty("serviceAccounts");
        Assert.Equal(1, accounts.GetArrayLength());
        var account = accounts[0];
        Assert.Equal("userorg", account.GetProperty("name").GetString());
        Assert.Equal("userorg", account.GetProperty("keycloakClientId").GetString());
        Assert.Equal("account-secret", account.GetProperty("secret").GetString());
        Assert.Contains("registry:read", account.GetProperty("permissions").Deserialize<string[]>()!);
    }

    [Fact]
    public void ImportDocument_lets_userorg_use_the_heappe_mock_client()
    {
        using var document = JsonDocument.Parse(NexusApiModes.ImportDocument("userorg", "account-secret", "http://heappe-mock:80"));

        var client = Assert.Single(
            document.RootElement.GetProperty("keycloakClients").Deserialize<JsonElement[]>()!,
            c => c.GetProperty("name").GetString() == "heappe-mock");
        Assert.Contains("userorg", client.GetProperty("allowedServices").Deserialize<string[]>()!);
    }

    [Fact]
    public void ImportDocument_uses_the_given_service_account_client()
    {
        using var document = JsonDocument.Parse(NexusApiModes.ImportDocument("userorg-dev", "account-secret", "http://heappe-mock:80"));

        var account = Assert.Single(document.RootElement.GetProperty("serviceAccounts").Deserialize<JsonElement[]>()!);
        Assert.Equal("userorg-dev", account.GetProperty("name").GetString());
        Assert.Equal("userorg-dev", account.GetProperty("clientId").GetString());
        Assert.Equal("userorg-dev", account.GetProperty("keycloakClientId").GetString());
        var client = Assert.Single(document.RootElement.GetProperty("keycloakClients").Deserialize<JsonElement[]>()!);
        Assert.Equal(["userorg-dev"], client.GetProperty("allowedServices").Deserialize<string[]>()!);
    }

    [Fact]
    public void ImportDocument_routes_the_heappe_mock_instance_to_the_upstream_url()
    {
        using var document = JsonDocument.Parse(NexusApiModes.ImportDocument("userorg", "account-secret", "http://heappe-mock:80"));

        var instance = Assert.Single(document.RootElement.GetProperty("proxyInstances").Deserialize<JsonElement[]>()!);
        Assert.Equal("heappe", instance.GetProperty("type").GetString());
        Assert.Equal("heappe-mock", instance.GetProperty("name").GetString());
        Assert.Equal("http://heappe-mock:80", instance.GetProperty("url").GetString());
        Assert.True(instance.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Container_mode_gives_nexusapi_its_own_postgres()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);

        Assert.NotEmpty(root.Calls("AddPostgres"));
        Assert.NotEmpty(root.Calls("AddDatabase"));
    }

    [Fact]
    public void Container_mode_runs_migrate_then_import_before_the_server()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);

        Assert.NotEmpty(root.Calls("WithArgs", "migrate"));
        Assert.NotEmpty(root.Calls("WithArgs", "import"));
        Assert.True(
            root.Calls("WaitForCompletion").Count >= 2,
            "the import waits for migrate and the server waits for the import (Q2)");
        Assert.NotEmpty(root.Calls("WithContainerFiles"));
    }

    [Fact]
    public void Container_mode_starts_the_server_without_migrations()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);

        var runMigrations = root.Call("WithEnvironment", "RUN_MIGRATIONS");
        Assert.Contains(
            runMigrations.ArgumentList.Arguments,
            argument => AppHostSyntax.StringValue(argument.Expression) == "false");
    }

    [Theory]
    [InlineData("AddNexusContainer")]
    [InlineData("AddNexusExternal")]
    public void Nexusapi_declares_no_http_health_check_because_production_maps_no_unauthenticated_endpoint(string wiringMethod)
    {
        var root = AppHostSource.RootOf(ExtensionsFile);
        var wiring = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method => method.Identifier.ValueText == wiringMethod);
        Assert.NotNull(wiring);

        Assert.Empty(wiring.Calls("WithHttpHealthCheck"));
    }

    [Fact]
    public void Image_tag_base_url_and_service_account_secret_are_parameters()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);

        Assert.NotEmpty(root.Calls("AddParameter", "NexusApiImage"));
        Assert.NotEmpty(root.Calls("AddParameter", "NexusApiImageTag"));
        Assert.NotEmpty(root.Calls("AddParameter", "NexusApiBaseUrl"));
        Assert.Contains(
            root.Calls("AddParameter"),
            call => call.ArgumentList.Arguments.Any(argument => argument.ToString().Contains("secret: true", StringComparison.Ordinal)));
        Assert.NotEmpty(root.Calls("AddParameter", "NexusApiServiceAccountClient"));
    }

    [Fact]
    public void Default_image_tag_is_the_published_pinned_version_not_latest()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);
        var field = root.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Single(declaration => declaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "DefaultImageTag"));
        var tag = AppHostSyntax.StringValue(field.Declaration.Variables.Single().Initializer!.Value);

        // NexusApi's CI pushes only `<next>-rc.<count>` and `X.Y.Z`, never `latest`, so `latest` is a manifest that does not exist.
        Assert.Equal("0.0.0-rc.357", tag);
    }

    [Fact]
    public void External_mode_registers_nexusapi_as_an_external_service()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);

        Assert.NotEmpty(root.Calls("AddExternalService", "nexusapi"));
    }

    [Fact]
    public void Api_references_nexusapi_without_waiting_for_it()
    {
        var root = AppHostSource.RootOf(ExtensionsFile);
        var wiring = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(method => method.Identifier.ValueText == "WithNexusApi");
        Assert.NotNull(wiring);

        // No WaitFor (Q1): the rest of the host must still start when the NexusApi image is missing.
        Assert.Empty(wiring.Calls("WaitFor"));
        Assert.NotEmpty(wiring.Calls("WithReference").Concat(wiring.Calls("WithEnvironment")));
        Assert.Contains(AppHostSource.Program.Calls("WithNexusApi"), call => call.Receiver() == "api" || call.ArgumentList.Arguments.Any(a => a.ToString() == "api"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Token_exchange_flag_without_a_value_is_off(string? configured) =>
        Assert.False(ParseTokenExchangeEnabled()(configured));

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData(" true ", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Token_exchange_flag_parses_a_plain_boolean_like_the_api(string configured, bool expected) =>
        Assert.Equal(expected, ParseTokenExchangeEnabled()(configured));

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("{ \"EnabledFor\": [] }")]
    public void Token_exchange_flag_that_is_not_a_boolean_fails_naming_the_parameter(string configured)
    {
        var parse = ParseTokenExchangeEnabled();

        var exception = Assert.Throws<InvalidOperationException>(() => parse(configured));

        Assert.Contains("FeatureTokenExchangeEnabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_exchange_flag_is_read_through_the_one_parse_rule()
    {
        var callers = ResourceCommandSyntax.AllRoots
            .Where(file => !file.Key.EndsWith("NexusApiMode.cs", StringComparison.Ordinal))
            .Where(file => file.Value.Calls("ParseTokenExchangeEnabled").Count > 0)
            .Select(file => file.Key)
            .ToList();

        Assert.True(callers.Count > 0, "no AppHost file reads FeatureTokenExchangeEnabled through NexusApiModes.ParseTokenExchangeEnabled");
    }

    [Fact]
    public void Api_gets_no_service_discovery_entry_for_nexusapi()
    {
        var offenders = ResourceCommandSyntax.AllRoots
            .Where(file => file.Value.Code().Contains("services__nexusapi__", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Key)
            .ToList();

        Assert.True(offenders.Count == 0, $"`services__nexusapi__*` is replaced by `TokenExchange__Nexus__BaseUrl`, still written in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Api_gets_no_old_Nexus_variables()
    {
        var offenders = ResourceCommandSyntax.AllRoots
            .Where(file => OldNexusKey().IsMatch(file.Value.Code()))
            .Select(file => file.Key)
            .ToList();

        Assert.True(offenders.Count == 0, $"the `Nexus__*` variables have no alias, `TokenExchange__Nexus__*` replaces them; still written in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Nexus_base_url_is_set_for_the_container_and_the_external_nexusapi_whatever_the_flag()
    {
        var assignments = ExternalServiceSyntax.AppHost.Environment("TokenExchange__Nexus__BaseUrl");
        var runMode = assignments.Where(assignment => assignment.IsInsideCallback && !assignment.Node.IsInsidePublishModeBranch()).ToList();

        Assert.True(
            runMode.Any(assignment => assignment.ValueCode.Contains("GetEndpoint", StringComparison.Ordinal)),
            "no run-mode `TokenExchange__Nexus__BaseUrl` assignment points at the NexusApi container's endpoint");
        Assert.True(
            runMode.Any(assignment => assignment.Value.ReadsWord("BaseUrl")),
            "no run-mode `TokenExchange__Nexus__BaseUrl` assignment carries the external NexusApi's base URL");
        Assert.All(assignments, assignment => Assert.True(
            assignment.Node.TokenExchangeGateOf() is null,
            $"the base URL is set whatever the token exchange flag, but {assignment.Label} is gated on it"));
    }

    [Theory]
    [InlineData("TokenExchange__Nexus__ClientId")]
    [InlineData("TokenExchange__Nexus__ClientSecret")]
    public void Nexus_service_account_is_set_in_run_mode_only_when_the_flag_is_on(string name)
    {
        var runMode = ExternalServiceSyntax.AppHost.Environment(name)
            .Where(assignment => !assignment.Node.IsInsidePublishModeBranch())
            .ToList();

        Assert.True(runMode.Count > 0, $"the AppHost no longer sets `{name}` under `aspire run`");
        Assert.All(runMode, AssertGatedOnTheFlagInACallback);
    }

    [Theory]
    [InlineData("TokenExchange__ClientId", "TokenExchangeClientId")]
    [InlineData("TokenExchange__ClientSecret", "TokenExchangeClientSecret")]
    [InlineData("TokenExchange__Authority", "Authority")]
    public void Exchange_client_is_set_in_run_mode_only_when_the_flag_is_on(string name, string externalSource)
    {
        var runMode = ExternalServiceSyntax.AppHost.Environment(name)
            .Where(assignment => !assignment.Node.IsInsidePublishModeBranch())
            .ToList();

        Assert.True(
            runMode.Any(assignment => assignment.Value.ReadsWord(externalSource)),
            $"no run-mode `{name}` assignment reads the external identity provider's {externalSource} any more");
        Assert.True(
            runMode.Any(assignment => !assignment.Value.ReadsWord(externalSource)),
            $"no run-mode `{name}` assignment carries the local Keycloak value any more");
        Assert.All(runMode, AssertGatedOnTheFlagInACallback);
    }

    [Theory]
    [InlineData("Idp__Authority")]
    [InlineData("Idp__Audience")]
    [InlineData("AuthenticationConfiguration__MetadataAddress")]
    public void Identity_provider_settings_do_not_depend_on_the_token_exchange_flag(string name)
    {
        var assignments = ExternalServiceSyntax.AppHost.Environment(name);

        Assert.True(assignments.Count > 0, $"the AppHost no longer sets `{name}`");
        Assert.All(assignments, assignment => Assert.True(
            assignment.Node.TokenExchangeGateOf() is null,
            $"{assignment.Label} is gated on the token exchange flag; the identity provider is used whatever it is"));
    }

    [Fact]
    public void Token_exchange_variables_are_set_only_on_the_api()
    {
        var assignments = ExternalServiceSyntax.AppHost.EnvironmentAssignments
            .Where(assignment => assignment.Name.StartsWith("TokenExchange__", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(assignments);
        Assert.All(assignments, assignment => Assert.True(
            ReceiverOf(assignment) == "api",
            $"{assignment.Label} is set on `{ReceiverOf(assignment)}`, not on the api"));
    }

    [Fact]
    public void Api_reads_the_token_exchange_flag_from_the_same_parameter()
    {
        var assignment = ExternalServiceSyntax.AppHost.SingleEnvironment("FeatureManagement__TokenExchangeEnabled");

        Assert.True(
            assignment.ValueCode.MentionsWord("FeatureTokenExchangeEnabled"),
            $"the api's flag no longer comes from the FeatureTokenExchangeEnabled parameter: {assignment.Label} = {assignment.ValueCode}");
    }

    [Fact]
    public void WithNexusApi_takes_the_token_exchange_flag()
    {
        var wiring = Method(ExtensionsFile, "WithNexusApi");

        var flag = wiring.ParameterList.Parameters.SingleOrDefault(parameter => parameter.Identifier.ValueText == "tokenExchangeEnabled");
        Assert.True(flag is not null, "WithNexusApi declares no `tokenExchangeEnabled` parameter");
        Assert.Equal("IResourceBuilder<ParameterResource>", flag.Type?.ToString());

        var call = Assert.Single(AppHostSource.Program.Calls("WithNexusApi"));
        Assert.True(
            call.ArgumentList.Arguments.Any(argument => argument.Expression.ReadsTokenExchangeFlag()),
            $"Program.cs does not pass FeatureTokenExchangeEnabled to WithNexusApi: {call.Describe()}");
    }

    [Fact]
    public void Selection_takes_the_token_exchange_flag_after_build()
    {
        var applyTo = Method(SelectionFile, "ApplyTo");

        var flag = applyTo.ParameterList.Parameters.SingleOrDefault(parameter => parameter.Identifier.ValueText == "tokenExchangeEnabled");
        Assert.True(flag is not null, "NexusApiSelection.ApplyTo declares no `tokenExchangeEnabled` parameter");
        Assert.Equal("ParameterResource", flag.Type?.ToString());

        var call = Assert.Single(AppHostSource.Program.Calls("ApplyTo"), candidate => candidate.Receiver() == "nexusApi");
        Assert.True(
            call.ArgumentList.Arguments.Any(argument => argument.Expression.ReadsTokenExchangeFlag()),
            $"Program.cs does not pass FeatureTokenExchangeEnabled to nexusApi.ApplyTo: {call.Describe()}");
    }

    [Fact]
    public void Selection_reads_the_flag_only_for_the_external_nexusapi()
    {
        var reads = AppHostSource.RootOf(SelectionFile).DescendantNodes()
            .OfType<AwaitExpressionSyntax>()
            .Where(read => read.Code().Contains(AppHostSyntax.TokenExchangeFlag, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(reads.Count > 0, "NexusApiSelection never reads the token exchange flag's value");
        Assert.All(reads, read => Assert.True(
            read.Ancestors().OfType<IfStatementSyntax>().Any(statement =>
                statement.Statement.Span.Contains(read.Span) && ExternalBranch().IsMatch(statement.Condition.Code())),
            $"the flag is read outside the `mode != NexusApiMode.Container` branch, so the container or a publish would wait on it: {read.Describe()}"));
    }

    [Fact]
    public void Selection_requires_the_service_account_only_when_the_flag_is_on()
    {
        var checks = AppHostSource.RootOf(SelectionFile).Calls("IsNullOrWhiteSpace")
            .Where(check => ServiceAccountValue().IsMatch(check.ArgumentList.Code()))
            .ToList();

        Assert.True(checks.Count > 0, "NexusApiSelection no longer checks the service-account client and secret");
        Assert.All(checks, check => Assert.True(
            check.GateOf(AppHostSyntax.ReadsTokenExchangeFlag) is not null,
            $"the service account is required whatever the token exchange flag: {check.Describe()}"));

        var ask = Assert.Single(AppHostSource.RootOf(SelectionFile).Calls("AskExternalAsync"));
        Assert.True(
            ask.ArgumentList.Arguments.Any(argument => argument.Expression.ReadsTokenExchangeFlag()),
            $"NexusApiSelection does not pass the token exchange flag to AskExternalAsync: {ask.Describe()}");
    }

    [Fact]
    public void Prompt_takes_whether_to_ask_for_the_service_account()
    {
        var ask = Method(PromptFile, "AskExternalAsync");

        var flag = ask.ParameterList.Parameters.SingleOrDefault(parameter => parameter.Identifier.ValueText == AskServiceAccount);
        Assert.True(flag is not null, "NexusApiPrompt.AskExternalAsync declares no `askServiceAccount` parameter");
        Assert.Equal("bool", flag.Type?.ToString());
        Assert.Equal("Task<(string? BaseUrl, string? ClientId, string? Secret)>", ask.ReturnType.ToString());
    }

    [Fact]
    public void Prompt_builds_reads_names_and_saves_the_service_account_only_when_asked()
    {
        var literals = StringLiterals(Method(PromptFile, "AskExternalAsync"));

        Assert.Contains(literals, literal => literal.Text == "NexusApiServiceAccountClient");
        Assert.Contains(literals, literal => literal.Text == "NexusApiServiceAccountSecret");
        Assert.Contains(literals, literal => literal.Text.Contains("Parameters:NexusApiServiceAccountClient", StringComparison.Ordinal) && !IsInsideTrySetSecret(literal.Node));
        Assert.All(
            literals.Where(literal => literal.Text.Contains("NexusApiServiceAccount", StringComparison.Ordinal)),
            literal => Assert.True(
                IsGatedOnAsking(literal.Node),
                $"the service account is built, read, named or saved without `askServiceAccount`: {literal.Node.Describe()}"));
    }

    [Fact]
    public void Prompt_always_asks_for_saves_and_names_the_base_url()
    {
        var ask = Method(PromptFile, "AskExternalAsync");
        var literals = StringLiterals(ask);

        Assert.Contains(literals, literal => literal.Text == "NexusApiBaseUrl" && !IsGatedOnAsking(literal.Node));
        Assert.Contains(
            literals,
            literal => literal.Text.Contains("Parameters:NexusApiBaseUrl", StringComparison.Ordinal)
                && !IsInsideTrySetSecret(literal.Node)
                && !IsGatedOnAsking(literal.Node));
        Assert.Contains(ask.Calls("TrySetSecret", "Parameters:NexusApiBaseUrl"), save => !IsGatedOnAsking(save));
    }

    [Fact]
    public void External_base_url_parameter_stays_required()
    {
        var declarations = ResourceCommandSyntax.CallsEverywhere("AddParameter")
            .Where(declaration => declaration.Call.ArgumentList.Arguments.Any(argument => AppHostSyntax.StringValue(argument.Expression) == "NexusApiBaseUrl"))
            .ToList();

        Assert.NotEmpty(declarations);
        Assert.All(declarations, declaration => Assert.Contains("Required=true", Compact(declaration.Call.Chain()), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TokenExchangeClientId")]
    [InlineData("TokenExchangeClientSecret")]
    public void Exchange_client_parameters_keep_their_empty_default(string parameter)
    {
        var declaration = AppHostSource.ParameterExtensions.Call("AddParameter", parameter);
        var code = Compact(declaration.Chain());

        Assert.Contains("newRuntimeDefault(\"\")", code, StringComparison.Ordinal);
        Assert.Contains("Required=false", code, StringComparison.Ordinal);
    }

    private const string SelectionFile = "NexusApiSelection.cs";

    private const string PromptFile = "NexusApiPrompt.cs";

    private const string AskServiceAccount = "askServiceAccount";

    private static Func<string?, bool> ParseTokenExchangeEnabled()
    {
        var method = typeof(NexusApiModes).GetMethod(
            "ParseTokenExchangeEnabled",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(string)]);

        Assert.True(
            method is not null && method.ReturnType == typeof(bool),
            "NexusApiModes (NexusApiMode.cs) declares no `static bool ParseTokenExchangeEnabled(string?)`, the AppHost's one rule for FeatureTokenExchangeEnabled");

        return method.CreateDelegate<Func<string?, bool>>();
    }

    private static MethodDeclarationSyntax Method(string file, string name)
    {
        var method = AppHostSource.RootOf(file).DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(candidate => candidate.Identifier.ValueText == name);

        Assert.True(method is not null, $"{file} no longer declares {name}");
        return method;
    }

    private static void AssertGatedOnTheFlagInACallback(ExternalServiceSyntax.EnvironmentAssignment assignment)
    {
        var gate = assignment.Node.TokenExchangeGateOf();

        Assert.True(
            gate is not null && gate.IsInsideCallback(),
            $"{assignment.Label} is not set from a resource-start callback that reads the token exchange flag and writes it only when the flag is on");
    }

    private static string? ReceiverOf(ExternalServiceSyntax.EnvironmentAssignment assignment) =>
        (assignment.Node as InvocationExpressionSyntax ?? assignment.Node.Ancestors().OfType<InvocationExpressionSyntax>()
            .LastOrDefault(call => AppHostSyntax.NameOf(call) == "WithEnvironment"))?.Receiver();

    private static bool IsGatedOnAsking(SyntaxNode node) =>
        node.GateOf(expression => expression.ReadsWord(AskServiceAccount)) is not null;

    private static List<(string Text, SyntaxNode Node)> StringLiterals(SyntaxNode scope) =>
        [.. scope.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => (literal.Token.ValueText, (SyntaxNode)literal))
            .Concat(scope.DescendantNodes()
                .OfType<InterpolatedStringTextSyntax>()
                .Select(text => (text.TextToken.ValueText, (SyntaxNode)text)))];

    private static bool IsInsideTrySetSecret(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().Any(call => AppHostSyntax.NameOf(call) == "TrySetSecret");

    private static string Compact(SyntaxNode node) => WhiteSpace().Replace(node.Code(), string.Empty);

    [GeneratedRegex(@"(?<![A-Za-z0-9_])Nexus__")]
    private static partial Regex OldNexusKey();

    [GeneratedRegex(@"mode\s*!=\s*NexusApiMode\.Container|mode\s+is\s+not\s+NexusApiMode\.Container|mode\s*==\s*NexusApiMode\.External")]
    private static partial Regex ExternalBranch();

    [GeneratedRegex(@"(?i)client|secret")]
    private static partial Regex ServiceAccountValue();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
