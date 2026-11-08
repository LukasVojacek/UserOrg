namespace UserOrg.AppHost.Tests;

public sealed class MigrationProjectResourceCharacterizationTests
{
    [Theory]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource, "ConnectionStrings__UserOrgDBContext")]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource, "ConnectionStrings__ForgeDBContext")]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource, "ConnectionStrings__WorkflowDBContext")]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource, "ConnectionStrings__UserOrgDBContext")]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource, "ConnectionStrings__ForgeDBContext")]
    [InlineData(MigrationProjectResourceSyntax.SeedResource, "ConnectionStrings__UserOrgDBContext")]
    [InlineData(MigrationProjectResourceSyntax.SeedResource, "ConnectionStrings__ForgeDBContext")]
    [InlineData(MigrationProjectResourceSyntax.SeedResource, "ConnectionStrings__WorkflowDBContext")]
    public void Should_KeepTheEnvironmentVariableOnTheMigrationTool(string resourceName, string variable)
    {
        var names = MigrationProjectResourceSyntax.Registration(resourceName).EnvironmentVariableNames();

        Assert.Contains(variable, names);
    }

    [Fact]
    public void Should_NotAddEnvironmentVariablesToTheMigrationTools()
    {
        Assert.Equal(3, MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.ApplyResource).EnvironmentVariableNames().Count);
        Assert.Equal(2, MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.ScriptResource).EnvironmentVariableNames().Count);
        Assert.Equal(3, MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.SeedResource).EnvironmentVariableNames().Count);
    }

    [Theory]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource)]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource)]
    public void Should_KeepTheWaitForSqlEdge(string resourceName)
    {
        var registration = MigrationProjectResourceSyntax.Registration(resourceName);

        Assert.Equal(["sql"], registration.ArgumentTextOf("WaitFor"));
    }

    [Fact]
    public void Should_KeepTheSeedToolWaitingForTheMigrationsToComplete()
    {
        var registration = MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.SeedResource);

        Assert.Equal(["migrationsApply"], registration.ArgumentTextOf("WaitForCompletion"));

        // The seed is ordered after the gateway process, which is where the RBAC import that fills
        // forge.Roles runs. What that edge is worth is pinned in
        // Should_DescribeTheSeedsGatewayEdgeAsAnOrderingEdgeOnly.
        Assert.Equal(["gateway"], registration.ArgumentTextOf("WaitFor"));
    }

    /// <summary>
    /// T-085 F8: the `WaitFor(gateway)` edge stays, but the comment on it may not claim it waits for a
    /// healthy gateway. `WaitFor` releases on Running for a resource with no health check, the AppHost
    /// registers no Aspire health check on the gateway, and `RbacSeedHostedService` writes the roles after
    /// Kestrel is already listening - `DatabaseSourceSelection.cs` records that edge releasing 12 ms before
    /// the first role was written. The seed's real precondition is rows in `forge.Roles`, and
    /// `DatabaseSourceSelection.WaitForRbacRolesAsync` is what holds for them.
    /// </summary>
    [Fact]
    public void Should_DescribeTheSeedsGatewayEdgeAsAnOrderingEdgeOnly()
    {
        var registration = MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.SeedResource);

        Assert.Equal(["gateway"], registration.ArgumentTextOf("WaitFor"));

        var seedTool = MigrationProjectResourceSyntax.MigrationTools.Method("AddSeedTool").ToString();

        Assert.DoesNotContain("reports healthy", seedTool, StringComparison.Ordinal);
        Assert.DoesNotContain("runs to completion before the Gateway", seedTool, StringComparison.Ordinal);

        Assert.Contains("WaitForRbacRolesAsync", seedTool, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource, "sql")]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource, "sql")]
    [InlineData(MigrationProjectResourceSyntax.SeedResource, "db")]
    public void Should_KeepTheParentRelationship(string resourceName, string parent)
    {
        var registration = MigrationProjectResourceSyntax.Registration(resourceName);

        Assert.Equal([parent], registration.ArgumentTextOf("WithParentRelationship"));
    }

    [Theory]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource)]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource)]
    [InlineData(MigrationProjectResourceSyntax.SeedResource)]
    public void Should_KeepExplicitStart(string resourceName)
    {
        var root = MigrationProjectResourceSyntax.MigrationTools;
        var registration = MigrationProjectResourceSyntax.Registration(resourceName);

        Assert.True(
            registration.AppliesToSameResource(root, "WithExplicitStart"),
            $"`{resourceName}` lost WithExplicitStart");
    }

    /// <summary>
    /// Script generation and the E2E seed are developer tools; applying migrations is a step a deployed
    /// topology needs, and `userorg` and `gateway` both WaitForCompletion on it - an edge a published
    /// compose writes as a depends_on, which docker refuses to load when it names no service.
    /// </summary>
    [Theory]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource, true)]
    [InlineData(MigrationProjectResourceSyntax.SeedResource, true)]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource, false)]
    public void Should_PublishOnlyTheMigrationStepADeploymentNeeds(string resourceName, bool excluded)
    {
        var root = MigrationProjectResourceSyntax.MigrationTools;
        var registration = MigrationProjectResourceSyntax.Registration(resourceName);

        Assert.True(
            registration.AppliesToSameResource(root, "ExcludeFromManifest") == excluded,
            excluded
                ? $"`{resourceName}` is a developer tool and must not reach a published manifest"
                : $"`{resourceName}` lost its place in the manifest, so every depends_on naming it breaks");
    }

    [Fact]
    public void Should_KeepTheHeappeMockUrlFallbackInTheSeedArgsCallback()
    {
        var registration = MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.SeedResource);

        var callbacks = registration.Chain().Calls("WithArgs")
            .SelectMany(call => call.LambdaArguments())
            .ToList();

        var callback = Assert.Single(callbacks);
        var code = callback.Code();

        Assert.Contains("__HEAPPE_MOCK_URL__", code, StringComparison.Ordinal);
        Assert.Contains("heappeMock", code, StringComparison.Ordinal);
        Assert.Contains("GetEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("NonMockHeappeEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("is not null", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheSeedToolUsableWithoutTheHeappeMock()
    {
        var root = MigrationProjectResourceSyntax.MigrationTools;
        var parameterType = root.Method("AddSeedTool").ParameterType("heappeMock");

        Assert.Equal("IResourceBuilder<WireMockServerResource>?", parameterType);

        var fallback = Assert.Single(
            root.DescendantNodes().OfType<VariableDeclaratorSyntax>(),
            declarator => declarator.Identifier.ValueText == "NonMockHeappeEndpoint");

        Assert.Contains("http://test-hpc.example.com", fallback.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheSeedScriptPointingAtTheE2EFixture()
    {
        var declaration = MigrationProjectResourceSyntax.MigrationTools.Method("AddSeedTool");

        Assert.Contains("seed-data.sql", declaration.Code(), StringComparison.Ordinal);
        Assert.Contains("E2E.Fixtures", declaration.Code(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("db-migrations-list")]
    [InlineData("db-migrations-check")]
    public void Should_KeepTheDisabledMigrationToolsCommentedOut(string resourceName)
    {
        var text = AppHostSource.TextOf(MigrationProjectResourceSyntax.MigrationToolFile);

        Assert.Contains(resourceName, text, StringComparison.Ordinal);

        Assert.False(
            MigrationProjectResourceSyntax.MigrationTools.Code().Contains(resourceName, StringComparison.Ordinal),
            $"`{resourceName}` was revived; the block leaves it commented out");

        Assert.Empty(MigrationProjectResourceSyntax.Registrations(resourceName));
    }

    [Fact]
    public void Should_KeepTheSeedToolWiredToTheMigrationToolsResult()
    {
        var program = AppHostSource.Program;
        var migrations = program.Call("AddMigrationTools");
        var seed = program.Call("AddSeedTool");

        var variable = migrations.AssignedVariable();
        Assert.NotNull(variable);
        Assert.Equal(variable, seed.ArgumentList.Arguments[0].Expression.ToString());
    }

    [Fact]
    public void Should_KeepDatabaseSourceSelectionDrivingMigrationApplyByCommand()
    {
        var code = MigrationProjectResourceSyntax.DatabaseSourceSelection.Code();

        Assert.Contains("ResourceCommandService", code, StringComparison.Ordinal);
        Assert.Contains("KnownResourceCommands.StartCommand", code, StringComparison.Ordinal);
        Assert.Contains("KnownResourceStates.NotStarted", code, StringComparison.Ordinal);
        Assert.Contains("KnownResourceStates.Finished", code, StringComparison.Ordinal);
        Assert.Contains("ExitCode = 0", code, StringComparison.Ordinal);
        Assert.Contains("PublishUpdateAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheLifecycleCommandDrivingTheToolsToCompletion()
    {
        var code = MigrationProjectResourceSyntax.ResourceExtensions.Method("RunToCompletionAsync").Code();

        Assert.Contains("ExecuteCommandAsync", code, StringComparison.Ordinal);
        Assert.Contains("KnownResourceCommands.StartCommand", code, StringComparison.Ordinal);
        Assert.Contains("KnownResourceStates.TerminalStates", code, StringComparison.Ordinal);
        Assert.Contains("tool.Resource.Name", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepDatabaseMigrationsAsAProjectReferenceOfTheAppHost()
    {
        var csproj = File.ReadAllText(Path.Combine(AppHostSource.Directory, "UserOrg.AppHost.csproj"));

        Assert.Contains("DatabaseMigrations.csproj", csproj, StringComparison.Ordinal);
    }
}
