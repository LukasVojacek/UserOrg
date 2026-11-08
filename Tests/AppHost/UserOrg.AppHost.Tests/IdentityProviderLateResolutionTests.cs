using System.Text.Json;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// T-085: the identity provider of a run is a dialog answered after <c>Build()</c>, not a switch read while
/// the model is built. Both providers are therefore always in the model, Keycloak is explicit-start, and
/// every login the dialog's local branch offers has a database row behind it.
/// </summary>
public sealed class IdentityProviderLateResolutionTests
{
    private const string RealmPath = "externalServices/keycloakRealm/master-realm.json";

    private static readonly string SeedScript = File.ReadAllText(Path.Combine(
        AppHostSource.Directory, "..", "..", "Tests", "E2E", "E2E.Fixtures", "seed-data.sql"));

    /// <summary>
    /// The seed script reaches the three original ids through <c>__PROJECT_*_IDP_ID__</c> placeholders the
    /// AppHost substitutes, so the literal has to be looked for in both files.
    /// </summary>
    private static readonly string SeededIdentities =
        SeedScript + AppHostSource.TextOf("MigrationToolExtssensions.cs");

    [Fact]
    public void Should_AskForTheIdentityProviderThroughTheInteractionService()
    {
        var prompt = AppHostSource.TextOf("RunSourcesDialog.cs");

        Assert.Contains("PromptInputsAsync", prompt, StringComparison.Ordinal);
        Assert.Contains("InputType.Choice", prompt, StringComparison.Ordinal);
        Assert.Contains("LocalKeycloak", prompt, StringComparison.Ordinal);
        Assert.Contains("ExternalAuthority", prompt, StringComparison.Ordinal);
    }

    /// <summary>The prompt is dashboard-only, so `aspire publish` and `aspire deploy` must not reach it.</summary>
    [Fact]
    public void Should_CheckTheInteractionServiceIsAvailableBeforeAsking()
    {
        Assert.Contains("IsAvailable", AppHostSource.TextOf("RunSourcesDialog.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A run-time answer cannot decide which parameters the model carries, so the switch that used to gate
    /// the external identity provider's parameters is gone and they are registered unconditionally.
    /// </summary>
    [Fact]
    public void Should_NotSwitchTheExternalIdentityProviderParametersOnAddParameters()
    {
        var addParameters = AppHostCompositionCharacterizationTests.AddParametersDeclaration();

        Assert.DoesNotContain(addParameters.ParameterList.Parameters, parameter =>
            parameter.Identifier.ValueText == "includeExternalIdentityProviderParameters");

        Assert.Contains("AddExternalService(\"idp-authority\"", AppHostSource.TextOf("IDistributedApplicationBuilderExtensions.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Keycloak is registered whatever the answer turns out to be, and explicit-start is what keeps it
    /// stopped on the external authority.
    /// </summary>
    [Fact]
    public void Should_RegisterKeycloakAsAnExplicitStartResource()
    {
        var keycloak = AppHostSource.RootOf("KeycloakExtensions.cs").Call("AddKeycloak", "Idp");

        Assert.True(
            keycloak.AppliedCalls("WithExplicitStart").Count == 1,
            "the Keycloak container is not explicit-start, so the identity-provider choice can no longer keep it stopped when the run uses the external authority");
    }

    /// <summary>
    /// The dialog is answered while the host runs, so nothing may branch on the answer at model-build time.
    /// </summary>
    [Fact]
    public void Should_NotBranchOnTheIdentityProviderWhileTheModelIsBuilt()
    {
        var program = AppHostSource.ProgramText;

        Assert.DoesNotContain("if (useLocalAuthority)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameters:UseLocalAuthority", program, StringComparison.Ordinal);
        Assert.Contains("UseLocalKeycloakAsync", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// `gateway.WaitFor(keycloak)` is gone — it would gate the gateway on a container the external branch
    /// never starts. What replaces it is the answer completing only once Keycloak is healthy.
    /// </summary>
    [Fact]
    public void Should_OrderTheGatewayBehindAHealthyKeycloakWithoutAWaitEdge()
    {
        var gates = ResourceCommandSyntax.CallsEverywhere("WaitFor")
            .Where(call => call.Call.ArgumentList.Code().MentionsWord("keycloak"))
            .ToList();

        Assert.True(
            gates.Count == 0,
            "the AppHost waits on the Keycloak container, which the external-authority branch never starts: "
                + string.Join("; ", gates.Select(gate => gate.File)));

        Assert.Contains(
            "WaitForResourceHealthyAsync",
            AppHostSource.TextOf("IdentityProviderSelection.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The seed resolves every role id out of forge.Roles, which only the Gateway fills, so it has to wait
    /// for the Gateway and not only for the migrations.
    /// </summary>
    [Fact]
    public void Should_OrderTheSeedBehindTheGatewaysRbacImport()
    {
        var seed = AppHostSource.RootOf("MigrationToolExtssensions.cs").Call("AddProject", "db-seed-e2e");

        Assert.True(
            seed.AppliedCalls("WaitFor").Any(call => call.ArgumentList.Code().MentionsWord("gateway")),
            "db-seed-e2e no longer waits for the gateway; the RBAC import that fills forge.Roles runs there, and without the edge the seed races it and THROWs 50002/50005/50006");
    }

    /// <summary>
    /// A migrated but empty database answers 403 on every mapped endpoint, so the local-container branch
    /// runs the seed itself rather than leaving it to whoever finds the dashboard command.
    /// </summary>
    [Fact]
    public void Should_StartTheSeedWithTheLocalDatabaseChain()
    {
        Assert.Contains(
            "StartAsync(commands, notifications, seedTool.Resource, cancellationToken)",
            AppHostSource.TextOf("DatabaseSourceSelection.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// `WaitFor(gateway)` is not evidence the RBAC import has run: a project resource is ready when its
    /// process is up, and RbacSeedHostedService runs after Kestrel is already listening. The seed's
    /// precondition is rows in forge.Roles, so the rows are what the app host waits for.
    /// </summary>
    [Fact]
    public void Should_WaitForTheRbacRolesBeforeStartingTheSeed()
    {
        var selection = AppHostSource.TextOf("DatabaseSourceSelection.cs");

        Assert.Contains("[forge].[Roles]", selection, StringComparison.Ordinal);

        Assert.True(
            selection.IndexOf("WaitForRbacRolesAsync(db.Resource", StringComparison.Ordinal)
                < selection.IndexOf("StartAsync(commands, notifications, seedTool.Resource", StringComparison.Ordinal),
            "the seed is started before the app host has confirmed forge.Roles is populated, so it races the gateway's RBAC import and THROWs 50002/50005/50006");
    }

    /// <summary>
    /// The realm and the seed are one chain: a gettoken login is only usable when the Keycloak user id it
    /// authenticates as is an <c>IdentityProviderUserId</c> the seed inserts.
    /// </summary>
    [Fact]
    public void Should_BackEveryGetTokenLoginWithARealmUserAndASeedRow()
    {
        var realmUsers = RealmUsers();
        var logins = GetTokenLogins();

        Assert.True(logins.Count >= 8, $"the gettoken dropdown offers only {logins.Count} seeded logins");

        foreach (var login in logins)
        {
            Assert.True(
                realmUsers.TryGetValue(login, out var id),
                $"the gettoken dropdown offers '{login}', which the imported realm does not create");

            Assert.True(
                SeededIdentities.Contains(id, StringComparison.OrdinalIgnoreCase),
                $"the realm gives '{login}' the user id {id}, which is the `sub` of its tokens, but seed-data.sql inserts no user with that IdentityProviderUserId — every mapped endpoint would answer 403 for this login");
        }
    }

    /// <summary>
    /// ProviderAdmin is scoped to the provider it is assigned to; the role without the assignment resolves
    /// to no provider and cannot exercise a single provider endpoint.
    /// </summary>
    [Fact]
    public void Should_AssignAProviderToTheProviderAdminSeedUser()
    {
        Assert.Contains("[AssignedProviderId]", SeedScript, StringComparison.Ordinal);
    }

    /// <summary>
    /// A token has to outlive the poking it is minted for. Keycloak caps an access token at what is left
    /// of the SSO session, so the idle window has to cover the lifespan or the realm hands out something
    /// shorter than it advertises.
    /// </summary>
    [Fact]
    public void Should_GiveTheRealmTokensAUsableLifetime()
    {
        using var realm = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppHostSource.Directory, RealmPath)));

        var lifespan = realm.RootElement.GetProperty("accessTokenLifespan").GetInt32();
        var idleTimeout = realm.RootElement.GetProperty("ssoSessionIdleTimeout").GetInt32();

        Assert.True(lifespan >= 3600, $"the realm mints tokens that live {lifespan}s; an hour is the floor");

        Assert.True(
            idleTimeout >= lifespan,
            $"the SSO session goes idle after {idleTimeout}s but the token claims {lifespan}s; Keycloak caps the token at the session, so the realm would hand out less than it advertises");
    }

    /// <summary>
    /// The gateway provisions an unknown caller just-in-time with a fresh Id, and
    /// UX_User_IdentityProviderUserId is unique on the subject alone, so the seed's own INSERT collides
    /// with that row and the whole script dies. It has to reconcile on the subject, not on the id it
    /// would like the row to have.
    /// </summary>
    [Fact]
    public void Should_NotSeedASecondIdentityForTheSameSubject()
    {
        Assert.Contains(
            "INNER JOIN [dbo].[User] u ON u.[IdentityProviderUserId] = c.IdpId",
            SeedScript,
            StringComparison.Ordinal);

        // The three canonical ids are named by later statements, so those rows cannot adopt; a foreign
        // row holding one of their subjects is removed instead.
        Assert.Contains("@seededIdentities", SeedScript, StringComparison.Ordinal);

        Assert.Contains(
            "INNER JOIN @seededIdentities s ON s.[IdpId] = u.[IdentityProviderUserId]",
            SeedScript,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The gateway's port is allocated per run and the parameter resolves before any endpoint exists, so
    /// no value that could be set here stays right for longer than one run. It is therefore not offered
    /// under `aspire run` at all: unset, the document carries no servers entry and ASP.NET Core writes
    /// one per request from the host header, which is always same-origin with the explorer page.
    /// </summary>
    [Fact]
    public void Should_NotOfferAScalarServerUrlSettingToALocalRun()
    {
        var parameters = AppHostSource.TextOf("IDistributedApplicationBuilderExtensions.cs");

        Assert.DoesNotContain("gateway-aspire.dev.localhost:5800", parameters, StringComparison.Ordinal);

        Assert.Contains(
            "builder.ExecutionContext.IsPublishMode\n                ? builder.AddParameter(\"ScalarServerUrl\"",
            parameters.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

        // Nothing in run mode may put a value behind it either.
        Assert.DoesNotContain("Scalar__ServerUrl", AppHostSource.ProgramText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The API explorer runs in the browser, so a request it never sends leaves no trace in any log this
    /// app host already collects. Browser logs put that console into the gateway's own log stream.
    /// </summary>
    [Fact]
    public void Should_CollectBrowserLogsForTheGateway()
    {
        var gateway = AppHostSource.Program.Call("AddProject", "gateway");

        Assert.True(
            gateway.AppliedCalls("WithBrowserLogs").Count == 1,
            "the gateway resource no longer collects browser logs");

        Assert.Contains(
            "Aspire.Hosting.Browsers",
            File.ReadAllText(Path.Combine(AppHostSource.Directory, "UserOrg.AppHost.csproj")),
            StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetTokenLogins()
    {
        var keycloak = AppHostSource.RootOf("KeycloakExtensions.cs");

        var seededLogins = keycloak.DescendantNodes()
            .OfType<CollectionExpressionSyntax>()
            .FirstOrDefault(collection => collection.Elements.Count > 3
                && collection.Elements.All(element => element.ToString().StartsWith("new(\"", StringComparison.Ordinal)));

        Assert.NotNull(seededLogins);

        return
        [
            .. seededLogins.Elements
                .Select(element => element.ToString())
                .Select(code => code[(code.IndexOf('"', StringComparison.Ordinal) + 1)..])
                .Select(code => code[..code.IndexOf('"', StringComparison.Ordinal)])
        ];
    }

    private static Dictionary<string, string> RealmUsers()
    {
        using var realm = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppHostSource.Directory, RealmPath)));

        var users = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var user in realm.RootElement.GetProperty("users").EnumerateArray())
        {
            users[user.GetProperty("username").GetString() ?? string.Empty] =
                user.GetProperty("id").GetString() ?? string.Empty;
        }

        return users;
    }

    /// <summary>
    /// Every question of the startup dialog opens on the local option.
    /// </summary>
    /// <remarks>
    /// The local container is the one a run can use without anything else being filled in: the external
    /// authority answers nothing until the IdpAuthority parameters are set, and the external connection
    /// string reaches no database until UserorgDev is. Pre-selecting them makes Enter the wrong answer.
    /// </remarks>
    [Theory]
    [InlineData("DatabaseSourceSelection.LocalContainer")]
    [InlineData("IdentityProviderSelection.LocalKeycloak")]
    [InlineData("nameof(NexusApiMode.Container)")]
    public void Should_OpenTheStartupDialogOnTheLocalOption(string expected)
    {
        Assert.Contains($"Value = {expected},", AppHostSource.TextOf("RunSourcesDialog.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The database, the identity provider and the NexusApi are answered in one dialog, shared by all three
    /// selections, so none of them can raise a dialog of its own after the first one closes.
    /// </summary>
    [Theory]
    [InlineData("DatabaseSourceSelection.cs")]
    [InlineData("IdentityProviderSelection.cs")]
    [InlineData("NexusApiSelection.cs")]
    public void Should_AnswerEverySelectionFromTheOneRunSourcesDialog(string selection)
    {
        var dialog = AppHostSource.RootOf("RunSourcesDialog.cs");

        Assert.Contains("dialog.AnswersAsync(", AppHostSource.TextOf(selection), StringComparison.Ordinal);
        Assert.Single(dialog.Calls("PromptInputsAsync"));
        Assert.Contains("_answers ??=", AppHostSource.TextOf("RunSourcesDialog.cs"), StringComparison.Ordinal);
    }
}
