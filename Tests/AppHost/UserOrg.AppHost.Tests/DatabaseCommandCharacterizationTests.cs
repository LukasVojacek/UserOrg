namespace UserOrg.AppHost.Tests;

/// <summary>
/// Characterizes the dashboard commands the SQL Server resource carries today, plus the seed executable
/// the new lifecycle command drives — the parts T-071-03 must leave standing.
/// </summary>
/// <remarks>
/// These are green today and must stay green. <c>SnapshotDatabase</c> and <c>RevertToSnapshot</c> are
/// explicitly out of the block's scope, <c>RestoreDevDatabase</c> is only to be wrapped in a progress
/// dialog (asserted as a red test in <see cref="DatabaseLifecycleCommandTests"/>), and
/// <c>db-seed-e2e</c> keeps the explicit-start-after-migrations shape block 02 gave it, because that is
/// what makes the <c>Seed</c> mode a start-and-wait rather than a fresh registration.
/// </remarks>
public sealed class DatabaseCommandCharacterizationTests
{
    /// <summary>The command pattern the block mirrors: health-gated, confirmed, never highlighted.</summary>
    [Theory]
    [InlineData("SnapshotDatabase")]
    [InlineData("RevertToSnapshot")]
    [InlineData("RestoreDevDatabase")]
    public void Should_KeepTheSqlCommandsHealthGatedAndConfirmed(string name)
    {
        var command = ResourceCommandSyntax.Command(name);
        var options = command.OptionsCode;

        Assert.Contains("UpdateState", options, StringComparison.Ordinal);
        Assert.Contains("HealthStatus.Healthy", options, StringComparison.Ordinal);
        Assert.Contains("ResourceCommandState.Disabled", options, StringComparison.Ordinal);
        Assert.NotEmpty(command.OptionValues("ConfirmationMessage"));
        Assert.DoesNotContain("IsHighlighted", options, StringComparison.Ordinal);
    }

    /// <summary>All three sit on the SQL Server container, in ResourceExtensions' extension block.</summary>
    [Theory]
    [InlineData("SnapshotDatabase")]
    [InlineData("RevertToSnapshot")]
    [InlineData("RestoreDevDatabase")]
    public void Should_DeclareTheSqlCommandsOnTheSqlServerResource(string name)
    {
        var command = ResourceCommandSyntax.Command(name);

        Assert.Equal("src/UserOrg.AppHost/ResourceExtensions.cs", command.File.Replace('\\', '/'));
        Assert.Contains(command.ReceiverTypes, type => type.Contains("SqlServerServerResource", StringComparison.Ordinal));
    }

    /// <summary>Out of scope for this block: the snapshot pair keeps its SQL verbatim.</summary>
    [Fact]
    public void Should_KeepTheSnapshotSqlUntouched()
    {
        var scripts = ResourceCommandSyntax.Command("SnapshotDatabase").Scripts;

        Assert.Contains(scripts, script => script.Contains("AS SNAPSHOT OF", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains("FROM DATABASE_SNAPSHOT", StringComparison.Ordinal));
    }

    /// <summary>The SQL-command shape the new command mirrors: connection, non-query, success/failure result.</summary>
    [Fact]
    public void Should_KeepTheSnapshotSqlExecutionShape()
    {
        var executor = Assert.Single(
            AppHostSource.RootOf("ResourceExtensions.cs").DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == "ExecuteSnapshotSqlAsync");

        var code = executor.Code();

        Assert.Contains("ConnectionStringExpression.GetValueAsync", code, StringComparison.Ordinal);
        Assert.Contains("new SqlConnection", code, StringComparison.Ordinal);
        Assert.Contains("ExecuteNonQueryAsync", code, StringComparison.Ordinal);
        Assert.Contains("CommandResults.Success", code, StringComparison.Ordinal);
        Assert.Contains("CommandResults.Failure", code, StringComparison.Ordinal);
    }

    /// <summary>The restore command keeps the backup bind mount and its RESTORE FILELISTONLY diagnosis.</summary>
    [Fact]
    public void Should_KeepTheRestoreBackupWiring()
    {
        var restore = ResourceCommandSyntax.Command("RestoreDevDatabase");
        var code = restore.ImplementationCode;

        Assert.Contains("WithBindMount", code, StringComparison.Ordinal);
        Assert.Contains("\"dbbackup/\"", code, StringComparison.Ordinal);
        Assert.Contains("/var/opt/mssql/backup/", code, StringComparison.Ordinal);
        Assert.Contains(restore.Scripts, script => script.Contains("RESTORE FILELISTONLY", StringComparison.Ordinal));
        Assert.Contains("CommandResults.Failure", restore.CallbackCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard the block extends to the progress dialogs: every interaction prompt the AppHost makes
    /// today sits behind <c>IInteractionService.IsAvailable</c>, because a command invoked from the CLI
    /// has no dashboard to prompt on.
    /// </summary>
    [Theory]
    [InlineData("PromptNotificationAsync")]
    [InlineData("PromptConfirmationAsync")]
    public void Should_GuardEveryInteractionPromptOnAvailability(string prompt)
    {
        var prompts = ResourceCommandSyntax.CallsEverywhere(prompt);
        Assert.True(prompts.Count > 0, $"the AppHost no longer calls `{prompt}`, so this guard proves nothing");

        var unguarded = prompts
            .Where(call => !call.Call.IsGuardedOnInteractionAvailability())
            .Select(call => $"{call.File} {call.Call.Describe()}")
            .ToList();

        Assert.True(
            unguarded.Count == 0,
            $"`{prompt}` is called without an IInteractionService.IsAvailable guard:\n  {string.Join("\n  ", unguarded)}");
    }

    /// <summary>
    /// Block 02: db-seed-e2e is registered unconditionally, starts only on demand, and runs after the
    /// migrations have completed. The Seed mode starts this resource; it does not re-register it.
    /// </summary>
    [Fact]
    public void Should_KeepTheSeedExecutableExplicitStartAfterMigrations()
    {
        var root = AppHostSource.MigrationToolExtensions;
        var seed = MigrationProjectResourceSyntax.Registration("db-seed-e2e");

        Assert.True(seed.AppliesToSameResource(root, "WithExplicitStart"), "db-seed-e2e lost WithExplicitStart");
        Assert.True(seed.AppliesToSameResource(root, "WaitForCompletion"), "db-seed-e2e lost WaitForCompletion(db-migrations-apply)");
        Assert.True(seed.AppliesToSameResource(root, "ExcludeFromManifest"), "db-seed-e2e lost ExcludeFromManifest");

        var seedTool = AppHostSource.Program.Call("AddSeedTool");
        Assert.True(
            seedTool.IsBuildTimeUnconditional(),
            $"AddSeedTool is registered behind a build-time branch again: {string.Join(" && ", seedTool.BuildTimeConditions())}");
    }

    /// <summary>
    /// The seed executable's arguments are what the Seed mode ends up running; the block starts it, it
    /// does not re-shape it.
    /// </summary>
    [Fact]
    public void Should_KeepTheSeedExecutableArguments()
    {
        var seed = MigrationProjectResourceSyntax.Registration("db-seed-e2e").Chain();
        var declaration = Assert.Single(
            AppHostSource.MigrationToolExtensions.DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == "AddSeedTool");

        Assert.Contains("\"seed\"", seed.Code(), StringComparison.Ordinal);
        Assert.Contains("\"--script\"", seed.Code(), StringComparison.Ordinal);
        Assert.Contains("seed-data.sql", declaration.Code(), StringComparison.Ordinal);
    }

    /// <summary>db-migrations-apply is the resource the ApplyMigrations mode restarts.</summary>
    [Fact]
    public void Should_KeepTheMigrationApplyExecutableAvailableForRestart()
    {
        var root = AppHostSource.MigrationToolExtensions;
        var apply = MigrationProjectResourceSyntax.Registration("db-migrations-apply");

        Assert.True(apply.AppliesToSameResource(root, "WithExplicitStart"), "db-migrations-apply lost WithExplicitStart");
        Assert.True(apply.AppliesToSameResource(root, "WaitFor"), "db-migrations-apply lost WaitFor(sql)");
    }
}
