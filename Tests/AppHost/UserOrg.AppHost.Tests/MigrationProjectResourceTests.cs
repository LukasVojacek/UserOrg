namespace UserOrg.AppHost.Tests;

public sealed class MigrationProjectResourceTests
{
    [Theory]
    [InlineData(MigrationProjectResourceSyntax.ApplyResource)]
    [InlineData(MigrationProjectResourceSyntax.ScriptResource)]
    [InlineData(MigrationProjectResourceSyntax.SeedResource)]
    public void Should_RegisterTheMigrationToolAsTheDatabaseMigrationsProject(string resourceName)
    {
        var registration = MigrationProjectResourceSyntax.Registration(resourceName);

        Assert.True(
            registration.FactoryName() == "AddProject",
            $"`{resourceName}` is registered with `{registration.FactoryName()}`, not `AddProject`: {registration.Describe()}");

        Assert.Equal(MigrationProjectResourceSyntax.ProjectMetadataType, registration.GenericTypeArgument());
    }

    [Fact]
    public void Should_NotRegisterAnythingWithAddExecutableInTheMigrationToolFile()
    {
        var executables = MigrationProjectResourceSyntax.MigrationTools
            .Calls("AddExecutable")
            .Select(call => call.Describe())
            .ToList();

        Assert.True(
            executables.Count == 0,
            $"{MigrationProjectResourceSyntax.MigrationToolFile} still registers executables:\n  "
                + string.Join("\n  ", executables));
    }

    [Fact]
    public void Should_NotLeaveAnyDotnetRunExecutableResourceInTheAppHost()
    {
        var dotnetRun = ResourceCommandSyntax.CallsEverywhere("AddExecutable")
            .Where(entry => entry.Call.Code().Contains("\"dotnet\"", StringComparison.Ordinal))
            .Where(entry => entry.Call.Code().Contains("\"run\"", StringComparison.Ordinal))
            .Select(entry => $"{entry.File} {entry.Call.Describe()}")
            .ToList();

        Assert.True(
            dotnetRun.Count == 0,
            $"the AppHost still registers `dotnet run` executable resources:\n  {string.Join("\n  ", dotnetRun)}");
    }

    [Fact]
    public void Should_NotPassAWorkingDirectoryToTheMigrationTools()
    {
        var code = MigrationProjectResourceSyntax.MigrationTools.Code();

        Assert.False(
            code.Contains("workingDirectory", StringComparison.Ordinal),
            $"{MigrationProjectResourceSyntax.MigrationToolFile} still pins a working directory; a project resource runs from its own project directory");
    }

    [Fact]
    public void Should_KeepTheApplyArgumentShape()
    {
        var shape = MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.ApplyResource).ArgumentShape();

        Assert.Equal(["apply"], shape);
    }

    [Fact]
    public void Should_KeepTheScriptArgumentShape()
    {
        var shape = MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.ScriptResource).ArgumentShape();

        Assert.Equal(["script", "--output", "script.sql"], shape);
    }

    [Fact]
    public void Should_KeepTheSeedArgumentShape()
    {
        var shape = MigrationProjectResourceSyntax.Registration(MigrationProjectResourceSyntax.SeedResource).ArgumentShape();

        Assert.Equal(
            [
                "seed",
                "--script",
                "--set", "__PROJECT_OWNER_IDP_ID__=a28127fd-1df5-4c56-be7e-f97953a19708",
                "--set", "__PROJECT_MANAGER_IDP_ID__=5a455b66-3abd-4bd1-b37f-80332572608f",
                "--set", "__PROJECT_MEMBER_IDP_ID__=f0e7217d-6c93-4ff7-9b8f-d1c43dd3df0b",
                "--set", "__SEED_OWNER_AS_ADMIN__=true",
            ],
            [.. shape.Where(argument => !argument.StartsWith('<'))]);

        var scriptIndex = shape.ToList().IndexOf("--script");
        Assert.True(scriptIndex >= 0 && scriptIndex + 1 < shape.Count, "the seed tool no longer passes `--script`");
        Assert.True(
            shape[scriptIndex + 1].StartsWith('<'),
            $"the seed script path is no longer the computed path expression: {shape[scriptIndex + 1]}");
    }

    [Fact]
    public void Should_ReturnAProjectResourceBuilderFromAddMigrationTools()
    {
        var returnType = MigrationProjectResourceSyntax.MigrationTools.Method("AddMigrationTools").ReturnType();

        Assert.False(
            MigrationProjectResourceSyntax.IsBoundToExecutableResource(returnType),
            $"AddMigrationTools still returns `{returnType}`, which no `AddProject` result can satisfy");

        Assert.True(
            MigrationProjectResourceSyntax.AcceptsAProjectResource(returnType),
            $"AddMigrationTools returns `{returnType}`, which is not a project-resource builder");
    }

    [Fact]
    public void Should_TakeAndReturnProjectResourceBuildersInAddSeedTool()
    {
        var method = MigrationProjectResourceSyntax.MigrationTools.Method("AddSeedTool");
        var parameterType = method.ParameterType("migrationsApply");
        var returnType = method.ReturnType();

        Assert.True(
            MigrationProjectResourceSyntax.AcceptsAProjectResource(parameterType),
            $"AddSeedTool still takes `{parameterType}` for migrationsApply");

        Assert.True(
            MigrationProjectResourceSyntax.AcceptsAProjectResource(returnType),
            $"AddSeedTool still returns `{returnType}`");
    }

    [Theory]
    [InlineData("ApplyTo", "migrationApply")]
    [InlineData("ResolveAndStartAsync", "migrationApply")]
    [InlineData("ReportSkippedAsync", "migrationApply")]
    public void Should_TakeAProjectResourceInDatabaseSourceSelection(string methodName, string parameterName)
    {
        var parameterType = MigrationProjectResourceSyntax.DatabaseSourceSelection
            .Method(methodName)
            .ParameterType(parameterName);

        Assert.False(
            MigrationProjectResourceSyntax.IsBoundToExecutableResource(parameterType),
            $"{MigrationProjectResourceSyntax.DatabaseSourceFile}: `{methodName}` still takes `{parameterType}` for {parameterName}, which the AddProject result cannot be passed to");

        Assert.True(
            MigrationProjectResourceSyntax.AcceptsAProjectResource(parameterType),
            $"{MigrationProjectResourceSyntax.DatabaseSourceFile}: `{methodName}` takes `{parameterType}` for {parameterName}");
    }

    [Theory]
    [InlineData("WithDatabaseLifecycleCommand", "migrationsApply")]
    [InlineData("WithDatabaseLifecycleCommand", "seedTool")]
    [InlineData("ExecuteDatabaseLifecycleAsync", "migrationsApply")]
    [InlineData("ExecuteDatabaseLifecycleAsync", "seedTool")]
    [InlineData("RunToCompletionAsync", "tool")]
    [InlineData("ResetAndSeedAsync", "migrationsApply")]
    [InlineData("ResetAndSeedAsync", "seedTool")]
    public void Should_TakeProjectResourcesInTheDatabaseLifecycleCommand(string methodName, string parameterName)
    {
        var parameterType = MigrationProjectResourceSyntax.ResourceExtensions
            .Method(methodName)
            .ParameterType(parameterName);

        Assert.False(
            MigrationProjectResourceSyntax.IsBoundToExecutableResource(parameterType),
            $"{MigrationProjectResourceSyntax.ResourceExtensionsFile}: `{methodName}` still takes `{parameterType}` for {parameterName}, which the AddProject result cannot be passed to");

        Assert.True(
            MigrationProjectResourceSyntax.AcceptsAProjectResource(parameterType),
            $"{MigrationProjectResourceSyntax.ResourceExtensionsFile}: `{methodName}` takes `{parameterType}` for {parameterName}");
    }
}
