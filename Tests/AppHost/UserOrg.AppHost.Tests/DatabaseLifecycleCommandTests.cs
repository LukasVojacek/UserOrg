using System.Text.RegularExpressions;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// T-071-03 (E3, E9): one <c>db-lifecycle</c> command on the SQL Server resource that resets, migrates
/// and seeds the local database, behind a progress dialog that survives a CLI invocation.
/// </summary>
/// <remarks>
/// <para>
/// Every test here fails against today's AppHost and passes against the shape the task's `## Plan`
/// describes. They assert the composition's <em>syntax</em> — see <see cref="AppHostSource"/> for why the
/// resource model is out of reach — so they pin the command's registration and the mechanism its
/// implementation uses, never the run.
/// </para>
/// <para>
/// The task's acceptance 1-4 are run-time statements: that each mode succeeds against a live SQL
/// container, that <c>ResetAndSeed</c> leaves exactly the seeded rows, that
/// <c>aspire resource userorgDatabase db-lifecycle --mode ApplyMigrations</c> does not throw on the
/// progress dialog, and that the confirmation dialog actually appears. None of them can be shown without
/// `aspire run` and a SQL container, and nothing here pretends otherwise.
/// </para>
/// </remarks>
public sealed class DatabaseLifecycleCommandTests
{
    private const string CommandName = "db-lifecycle";

    /// <summary>Plan step 1: one command, not one per mode.</summary>
    [Fact]
    public void Should_RegisterOneDatabaseLifecycleCommand()
    {
        var command = ResourceCommandSyntax.Command(CommandName);

        Assert.NotEmpty(command.OptionsCode);
    }

    /// <summary>Plan step 1: on the <c>userorgDatabase</c> SQL Server resource.</summary>
    [Fact]
    public void Should_DeclareTheLifecycleCommandOnTheSqlServerContainer()
    {
        var command = ResourceCommandSyntax.Command(CommandName);
        var program = AppHostSource.Program;
        var sql = program.Call("AddSqlServer", "userorgDatabase");

        if (command.DeclaringMethod is { } method)
        {
            Assert.Contains(
                command.ReceiverTypes,
                type => type.Contains("SqlServerServerResource", StringComparison.Ordinal));

            Assert.True(
                sql.AppliesToSameResource(program, method.Identifier.ValueText),
                $"`{method.Identifier.ValueText}` is never applied to the userorgDatabase SQL container");
        }
        else
        {
            Assert.True(
                command.Registration.Receiver() == "sql"
                    || command.Registration.Code().Contains("AddSqlServer", StringComparison.Ordinal),
                "the db-lifecycle command is registered on something other than the SQL container: "
                    + command.Registration.Describe());
        }
    }

    /// <summary>
    /// Plan step 1: <c>CommandOptions.Arguments</c> with a <c>Choice</c> input named <c>Mode</c> — the
    /// name the CLI's <c>--mode</c> maps onto.
    /// </summary>
    [Fact]
    public void Should_TakeTheModeAsAChoiceArgument()
    {
        var options = ResourceCommandSyntax.Command(CommandName).OptionsCode;

        Assert.Contains("Arguments", options, StringComparison.Ordinal);
        Assert.Contains("InputType.Choice", options, StringComparison.Ordinal);
        Assert.Contains("\"Mode\"", options, StringComparison.Ordinal);
    }

    /// <summary>Plan step 1: the four modes, and only ever as whole words — Reset is not ResetAndSeed.</summary>
    [Theory]
    [InlineData("Reset")]
    [InlineData("ApplyMigrations")]
    [InlineData("Seed")]
    [InlineData("ResetAndSeed")]
    public void Should_OfferTheFourLifecycleModes(string mode)
    {
        var command = ResourceCommandSyntax.Command(CommandName);
        var code = $"{command.OptionsCode}\n{command.ImplementationCode}";

        Assert.True(code.MentionsWord(mode), $"the db-lifecycle command does not know the `{mode}` mode");
    }

    /// <summary>
    /// Plan step 2: the destructive modes are confirmed before they run — either through the command's
    /// <c>ConfirmationMessage</c>, whose text then has to name what it is about to reset, or through an
    /// explicit confirmation prompt inside the callback.
    /// </summary>
    [Fact]
    public void Should_ConfirmTheDestructiveLifecycleModes()
    {
        var command = ResourceCommandSyntax.Command(CommandName);
        var confirmationMessages = command.OptionValues("ConfirmationMessage");
        var prompts = command.ImplementationCode.Contains("PromptConfirmationAsync", StringComparison.Ordinal);

        Assert.True(
            confirmationMessages.Count > 0 || prompts,
            "the db-lifecycle command asks for no confirmation before Reset / ResetAndSeed");

        if (confirmationMessages.Count > 0)
        {
            Assert.Contains(
                confirmationMessages,
                message => Regex.IsMatch(message, @"\breset\b", RegexOptions.IgnoreCase));
        }
    }

    /// <summary>Plan step 2: the same health gate the existing SQL commands carry.</summary>
    [Fact]
    public void Should_EnableTheLifecycleCommandOnlyWhileTheDatabaseIsHealthy()
    {
        var options = ResourceCommandSyntax.Command(CommandName).OptionsCode;

        Assert.Contains("UpdateState", options, StringComparison.Ordinal);
        Assert.Contains("HealthStatus.Healthy", options, StringComparison.Ordinal);
        Assert.Contains("ResourceCommandState.Disabled", options, StringComparison.Ordinal);
    }

    /// <summary>Plan step 2: the overflow menu, so no <c>IsHighlighted</c>.</summary>
    [Fact]
    public void Should_KeepTheLifecycleCommandInTheOverflowMenu()
    {
        var options = ResourceCommandSyntax.Command(CommandName).OptionsCode;

        Assert.DoesNotContain("IsHighlighted", options, StringComparison.Ordinal);
    }

    /// <summary>
    /// Plan step 4: <c>CommandResults.Success(message, result, resultFormat)</c> — the three-argument
    /// overload, so the dashboard has a payload naming what ran, not a bare success.
    /// </summary>
    [Fact]
    public void Should_ReturnASuccessPayloadNamingWhatRan()
    {
        var command = ResourceCommandSyntax.Command(CommandName);

        var payloads = command.DeclaringScope.Calls("Success")
            .Where(call => call.Code().Contains("CommandResults.Success", StringComparison.Ordinal))
            .Where(call => call.ArgumentList.Arguments.Count >= 3)
            .ToList();

        Assert.True(
            payloads.Count > 0,
            $"no CommandResults.Success(message, result, resultFormat) call in {command.File}");

        Assert.Contains(
            payloads,
            call => call.ArgumentList.Code().Contains("CommandResultFormat", StringComparison.Ordinal));
    }

    /// <summary>
    /// Plan step 1: <c>Reset</c> is a single script — single-user with rollback, drop, create — executed
    /// the way <c>ExecuteSnapshotSqlAsync</c> executes its SQL. Whether it leaves a usable database can
    /// only be seen against a live container (acceptance 1-2).
    /// </summary>
    [Fact]
    public void Should_ResetTheDatabaseWithASingleUserDropAndCreateScript()
    {
        var command = ResourceCommandSyntax.Command(CommandName);

        var resetScripts = command.Scripts
            .Where(script => Regex.IsMatch(script, @"ROLLBACK\s+IMMEDIATE", RegexOptions.IgnoreCase))
            .Where(script => Regex.IsMatch(script, @"DROP\s+DATABASE", RegexOptions.IgnoreCase))
            .Where(script => Regex.IsMatch(script, @"CREATE\s+DATABASE", RegexOptions.IgnoreCase))
            .ToList();

        Assert.True(
            resetScripts.Count > 0,
            $"no single reset script in {command.File} that sets SINGLE_USER WITH ROLLBACK IMMEDIATE, drops the database and creates it again");

        Assert.Contains("new SqlConnection", command.ImplementationCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Plan step 1: <c>ApplyMigrations</c> and <c>Seed</c> drive the existing explicit-start executables
    /// through <c>ResourceCommandService</c> rather than shelling out themselves, and wait for
    /// them to finish before reporting success.
    /// </summary>
    [Fact]
    public void Should_DriveMigrationsAndSeedThroughResourceCommandService()
    {
        var code = ResourceCommandSyntax.Command(CommandName).ImplementationCode;

        Assert.Contains("ResourceCommandService", code, StringComparison.Ordinal);
        Assert.Contains("ExecuteCommandAsync", code, StringComparison.Ordinal);
        Assert.Contains("WaitForResource", code, StringComparison.Ordinal);
        Assert.True(
            code.Contains("migration", StringComparison.OrdinalIgnoreCase),
            "the db-lifecycle command never reaches db-migrations-apply");
        Assert.True(
            code.Contains("seed", StringComparison.OrdinalIgnoreCase),
            "the db-lifecycle command never reaches db-seed-e2e");
    }

    /// <summary>Plan step 3: the new command runs behind a progress dialog.</summary>
    [Fact]
    public void Should_ShowAProgressDialogWhileTheLifecycleCommandRuns()
    {
        var code = ResourceCommandSyntax.Command(CommandName).ImplementationCode;

        Assert.True(
            code.Contains("PromptProgressAsync", StringComparison.Ordinal),
            "the db-lifecycle command runs without a progress dialog");
    }

    /// <summary>Plan step 3: so does the existing restore command, which today only notifies.</summary>
    [Fact]
    public void Should_ShowAProgressDialogWhileTheRestoreCommandRuns()
    {
        var restore = ResourceCommandSyntax.Command("RestoreDevDatabase");

        Assert.True(
            restore.CallbackCode.Contains("PromptProgressAsync", StringComparison.Ordinal),
            "the RestoreDevDatabase command still runs without a progress dialog");
    }

    /// <summary>
    /// Plan step 3: a command can be invoked from the CLI, where there is no dashboard to prompt on and
    /// the progress dialog throws — so every one of them is guarded by
    /// <c>IInteractionService.IsAvailable</c>. Whether the CLI invocation really survives is acceptance 3
    /// and needs `aspire run`.
    /// </summary>
    [Fact]
    public void Should_GuardEveryProgressDialogOnInteractionAvailability()
    {
        var prompts = ResourceCommandSyntax.CallsEverywhere("PromptProgressAsync");

        Assert.True(
            prompts.Count >= 2,
            $"expected a progress dialog on both db-lifecycle and RestoreDevDatabase, found {prompts.Count}");

        var unguarded = prompts
            .Where(prompt => !prompt.Call.IsGuardedOnInteractionAvailability())
            .Select(prompt => $"{prompt.File} {prompt.Call.Describe()}")
            .ToList();

        Assert.True(
            unguarded.Count == 0,
            "a progress dialog is prompted without an IInteractionService.IsAvailable guard:\n  "
                + string.Join("\n  ", unguarded));
    }
}
