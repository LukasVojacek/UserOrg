using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// T-071-01 (E1): the database-source choice must be resolved at RUN time, so the Aspire dashboard's
/// unresolved-parameter dialog actually selects the topology.
/// </summary>
/// <remarks>
/// <para>
/// Today <c>Program.cs</c> registers <c>UseLocalConnections</c> with <c>WithCustomInput</c> and then
/// reads it back through <c>builder.Configuration.GetValue("Parameters:UseLocalConnections")</c> in the
/// same breath. That read happens while the model is being built; parameter dialogs only resolve after
/// <c>Build()</c>. The branch is therefore always taken from user secrets or CLI args and the dialog is
/// decorative.
/// </para>
/// <para>
/// Every test in this class fails against that shape and passes against the one the task's `## Plan`
/// describes. They assert the composition's *syntax* — see <see cref="AppHostSource"/> for why the
/// resource model itself is out of reach — so they pin the mechanism (unconditional registration plus a
/// resource-start callback), not the runtime outcome. Acceptance 1-3 (the dialog, the ordering, the
/// *Save to user secret* checkbox) can only be shown by running `aspire run`; the task's `## Risks`
/// says so explicitly.
/// </para>
/// </remarks>
public sealed class DatabaseSourceLateResolutionTests
{
    private const string ConnectionStringPrefix = "ConnectionStrings__";

    /// <summary>
    /// T-085: the choice is a dialog the interaction service raises, not a parameter. A parameter dialog is
    /// answered when the dashboard shows it, which is before the answer is needed and gives the developer no
    /// way to be asked again; the interaction prompt is raised at the moment the source has to be decided.
    /// </summary>
    [Fact]
    public void Should_AskForTheDatabaseSourceThroughTheInteractionService()
    {
        var prompt = AppHostSource.TextOf("RunSourcesDialog.cs");

        Assert.Contains("PromptInputsAsync", prompt, StringComparison.Ordinal);
        Assert.Contains("InputType.Choice", prompt, StringComparison.Ordinal);
        Assert.Contains("LocalContainer", prompt, StringComparison.Ordinal);
        Assert.Contains("ExternalConnectionString", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// T-085: the prompt is dashboard-only and throws under `aspire publish` and `aspire deploy`, so the
    /// availability check is what keeps those two working.
    /// </summary>
    [Fact]
    public void Should_CheckTheInteractionServiceIsAvailableBeforeAsking()
    {
        var prompt = AppHostSource.TextOf("RunSourcesDialog.cs");

        Assert.Contains("IsAvailable", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// T-085: no DatabaseSource parameter any more — a parameter left without a value raises the dashboard's
    /// own unresolved-parameter dialog, which would ask the same question twice.
    /// </summary>
    [Fact]
    public void Should_NotRegisterADatabaseSourceParameter()
    {
        Assert.DoesNotContain(
            "AddParameter(\"DatabaseSource\")",
            AppHostSource.ProgramText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The defect itself: no configuration read of the database-source choice while the model is being built,
    /// because such a read resolves before any dialog can answer it.
    /// Checked over string literals only, so a comment mentioning the old key cannot fail this.
    /// T-085 exempts <c>RunSourcesDialog</c>, which reads the key after <c>Build()</c>, at the moment
    /// the answer is needed, as the fallback for a run that has nobody to answer the dialog.
    /// </summary>
    [Fact]
    public void Should_NotResolveTheDatabaseSourceAtModelBuildTime()
    {
        var offenders = AppHostSource.AllFiles
            .Where(file => !file.Key.EndsWith("RunSourcesDialog.cs", StringComparison.Ordinal))
            .SelectMany(file => AppHostSource.Parse(file.Value).GetRoot()
                .DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Where(literal => literal.Token.ValueText is "Parameters:UseLocalConnections" or "Parameters:DatabaseSource")
                .Select(literal => $"{file.Key} {literal.Describe()}"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"the database source is still read from configuration at model-build time:\n  {string.Join("\n  ", offenders)}");
    }

    /// <summary>Plan step 3: the SQL container is in the model whatever the choice turns out to be.</summary>
    [Fact]
    public void Should_RegisterTheSqlContainerUnconditionally()
    {
        var sql = AppHostSource.Program.Call("AddSqlServer", "userorgDatabase");

        Assert.True(
            sql.IsBuildTimeUnconditional(),
            "the SQL container is still registered behind a build-time branch: "
                + string.Join(" && ", sql.BuildTimeConditions()));
    }

    /// <summary>Plan step 3.</summary>
    [Fact]
    public void Should_RegisterTheDatabaseUnconditionally()
    {
        var database = AppHostSource.Program.Call("AddDatabase", "UserOrgDBContext");

        Assert.True(
            database.IsBuildTimeUnconditional(),
            "the UserOrgDBContext database is still registered behind a build-time branch: "
                + string.Join(" && ", database.BuildTimeConditions()));
    }

    /// <summary>Plan step 3: db-migrations-apply and db-migrations-script come with the container.</summary>
    [Fact]
    public void Should_RegisterTheMigrationToolsUnconditionally()
    {
        var migrations = AppHostSource.Program.Call("AddMigrationTools");

        Assert.True(
            migrations.IsBuildTimeUnconditional(),
            "the migration executables are still registered behind a build-time branch: "
                + string.Join(" && ", migrations.BuildTimeConditions()));
    }

    /// <summary>
    /// Plan step 3: DbGate comes with the container. It may stay gated on EphemeralContainers — that
    /// switch is explicitly preserved — but not on the database source.
    /// </summary>
    [Fact]
    public void Should_NotGateDbGateOnTheDatabaseSource()
    {
        var dbGate = AppHostSource.Program.Call("WithDbGate");

        Assert.False(
            dbGate.IsGatedOnDatabaseSource(),
            "DbGate is still registered behind the database-source branch: "
                + string.Join(" && ", dbGate.BuildTimeConditions()));
    }

    /// <summary>
    /// Plan step 3: unconditional in the model, but not started unless the resolved choice asks for it.
    /// </summary>
    [Fact]
    public void Should_GiveTheSqlContainerExplicitStart()
    {
        var program = AppHostSource.Program;
        var sql = program.Call("AddSqlServer", "userorgDatabase");

        Assert.True(
            sql.AppliesToSameResource(program, "WithExplicitStart"),
            "the SQL container has no WithExplicitStart, so it would start on its own in every run");
    }

    /// <summary>Plan step 3: db-migrations-apply must not run itself on an external database.</summary>
    [Fact]
    public void Should_GiveTheMigrationApplyExecutableExplicitStart()
    {
        var root = AppHostSource.MigrationToolExtensions;
        var apply = MigrationProjectResourceSyntax.Registration("db-migrations-apply");

        Assert.True(
            apply.AppliesToSameResource(root, "WithExplicitStart"),
            "db-migrations-apply has no WithExplicitStart, so it would run against whatever database is configured");
    }

    /// <summary>
    /// Plan step 4: every ConnectionStrings__* assignment on userorg and gateway moves into a callback.
    /// A two-argument WithEnvironment("ConnectionStrings__…", value) is evaluated while the model is
    /// built, which is exactly what makes the choice undecidable at that point.
    /// </summary>
    [Fact]
    public void Should_NotAssignConnectionStringsAtModelBuildTime()
    {
        var offenders = AppHostSource.Program.Calls("WithEnvironment")
            .Where(call => call.ArgumentList.Arguments.Count > 0)
            .Where(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression)
                ?.StartsWith(ConnectionStringPrefix, StringComparison.Ordinal) == true)
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"connection strings are still assigned at model-build time:\n  {string.Join("\n  ", offenders)}");
    }

    /// <summary>Plan step 4: one callback that writes all three contexts' connection strings.</summary>
    [Fact]
    public void Should_ResolveConnectionStringsInsideAResourceStartCallback()
    {
        Assert.NotEmpty(ConnectionStringCallbacks());
    }

    /// <summary>
    /// Plan step 4: the callback reads the resolved DatabaseSource — that resolution is the whole point,
    /// and it is only available once the parameter has been answered.
    /// </summary>
    [Fact]
    public void Should_ReadTheResolvedDatabaseSourceInsideTheCallback()
    {
        var callbacks = ConnectionStringCallbacks();

        Assert.Contains(
            callbacks,
            body => body.Contains("DatabaseSource", StringComparison.OrdinalIgnoreCase)
                && body.Contains("await", StringComparison.Ordinal));
    }

    /// <summary>
    /// Plan step 5: on LocalContainer the AppHost starts the explicit-start SQL chain itself, through
    /// ResourceCommandService. The ordering against the WaitForCompletion edges is verified by running
    /// the AppHost (task `## Risks`), not here.
    /// </summary>
    [Fact]
    public void Should_StartTheSqlChainThroughResourceCommandService()
    {
        Assert.Contains(
            AppHostSource.AllFiles,
            file => file.Value.Contains("ResourceCommandService", StringComparison.Ordinal));
    }

    /// <summary>
    /// Plan step 2: with both database parameters always registered, the two switches on AddParameters
    /// have nothing left to decide. includeExternalIdentityProviderParameters stays (characterized).
    /// </summary>
    [Fact]
    public void Should_CollapseTheDatabaseSwitchesFromAddParameters()
    {
        var parameters = AppHostCompositionCharacterizationTests.AddParametersDeclaration()
            .ParameterList.Parameters
            .Select(parameter => parameter.Identifier.ValueText)
            .ToList();

        Assert.DoesNotContain("includeUserorgDev", parameters);
        Assert.DoesNotContain("includeDatabasePassword", parameters);
    }

    /// <summary>Plan step 2: both database parameters are registered whatever the choice turns out to be.</summary>
    [Fact]
    public void Should_RegisterTheDatabaseParametersUnconditionally()
    {
        var root = AppHostSource.ParameterExtensions;

        foreach (var name in new[] { "UserorgDev", "DatabasePassword" })
        {
            var parameter = root.Call("AddParameter", name);
            Assert.True(
                parameter.IsBuildTimeUnconditional(),
                $"the {name} parameter is still registered behind a build-time branch: "
                    + string.Join(" && ", parameter.BuildTimeConditions()));
        }
    }

    /// <summary>
    /// The Vault address is not a choice at all: it is the local proxy on both database sources, so no
    /// build-time branch and no resolved-choice branch may reintroduce a remote one.
    /// </summary>
    [Fact]
    public void Should_NotBranchTheVaultAddressOnTheDatabaseSource()
    {
        Assert.DoesNotContain("RemoteVaultAddress", AppHostSource.ProgramText, StringComparison.Ordinal);

        // The exact key: "VaultCredentials__AddressPrefix" contains it as a prefix.
        var assignment = Assert.Single(
            AppHostSource.Program.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
            node => node.Left.ToString().Contains("\"VaultCredentials__Address\"", StringComparison.Ordinal));

        Assert.DoesNotContain("?", assignment.Right.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The bodies of the WithEnvironment callbacks that write every connection string.</summary>
    private static IReadOnlyList<string> ConnectionStringCallbacks() =>
        [.. AppHostSource.Program.Calls("WithEnvironment")
            .SelectMany(call => call.LambdaArguments())
            .Select(lambda => lambda.Body.ToString())
            .Where(body => body.Contains($"{ConnectionStringPrefix}UserOrgDBContext", StringComparison.Ordinal)
                && body.Contains($"{ConnectionStringPrefix}ForgeDBContext", StringComparison.Ordinal)
                && body.Contains($"{ConnectionStringPrefix}WorkflowDBContext", StringComparison.Ordinal))];
}
