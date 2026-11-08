using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Characterizes the parts of the AppHost's database composition that must survive T-071-01 unchanged.
/// </summary>
/// <remarks>
/// These are green today and must stay green: the block moves the database-source *decision* from
/// model-build time to run time, and is allowed to change nothing else about the SQL topology.
/// Everything the block deliberately changes is asserted in
/// <see cref="DatabaseSourceLateResolutionTests"/> instead, as a red test — so no test here is
/// expected to be edited by the implement phase.
/// </remarks>
public sealed class AppHostCompositionCharacterizationTests
{
    /// <summary>
    /// Acceptance 4: the `Testing` short-circuit is byte-identical to before. ADR-0069 makes the branch
    /// unreachable (nothing sets EnvironmentName == "Testing" any more), so it is preserved, not verified.
    /// If the implement phase concludes this block cannot stay verbatim — `useLocalConnections` loses its
    /// only reader — that contradicts acceptance 4 and belongs back with the coordinator, NOT in an edit
    /// to this test.
    /// </summary>
    [Fact]
    public void Should_KeepTheTestingShortCircuitByteIdentical()
    {
        const string shortCircuit = """
            if (builder.Environment.EnvironmentName == "Testing")
            {
                useLocalConnections = true;
                useLocalAuthority = true;
            }
            """;

        var program = AppHostSource.ProgramText.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(shortCircuit, program, StringComparison.Ordinal);
    }

    /// <summary>The substance of the same short-circuit, independent of the exact statements inside it.</summary>
    [Fact]
    public void Should_ShortCircuitOnTheTestingEnvironment()
    {
        var shortCircuit = Assert.Single(
            AppHostSource.Program.DescendantNodes().OfType<IfStatementSyntax>(),
            statement => statement.Condition.ToString() == "builder.Environment.EnvironmentName == \"Testing\"");

        Assert.Contains("useLocalAuthority = true;", shortCircuit.Statement.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard for every "must not appear" assertion in this suite: an absence is only evidence when
    /// the file actually parsed. If the AppHost ever uses syntax this Roslyn version cannot read, the
    /// negative tests would go vacuously green — this test goes red instead.
    /// </summary>
    [Fact]
    public void Should_ParseEveryAppHostSourceFileWithoutSyntaxErrors()
    {
        var errors = AppHostSource.AllFiles
            .SelectMany(file => AppHostSource.Parse(file.Value).GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{file.Key}: {diagnostic}"))
            .ToList();

        Assert.True(errors.Count == 0, string.Join("\n  ", errors));
    }

    [Fact]
    public void Should_KeepTheSqlServerContainerSettings()
    {
        var sql = AppHostSource.Program.Call("AddSqlServer", "userorgDatabase");
        var root = AppHostSource.Program;

        Assert.True(sql.AppliesToSameResource(root, "WithLifetime"), "the SQL container lost WithLifetime");
        Assert.True(sql.AppliesToSameResource(root, "WithImage"), "the SQL container lost WithImage");
        Assert.True(sql.AppliesToSameResource(root, "WithDatabaseRestoreCommand"), "the SQL container lost WithDatabaseRestoreCommand");
        Assert.True(sql.AppliesToSameResource(root, "WithSnapshotCommands"), "the SQL container lost WithSnapshotCommands");
    }

    [Fact]
    public void Should_PinTheSqlServerImage()
    {
        var image = AppHostSource.Program.Call("WithImage");

        Assert.Equal(
            ["mssql/server", "2025-latest"],
            [.. image.ArgumentList.Arguments.Select(argument => AppHostSyntax.StringValue(argument.Expression))]);
    }

    /// <summary>
    /// No resource asks the host for a port of its own choosing.
    /// </summary>
    /// <remarks>
    /// A fixed host port is a collision with whatever already holds it, and what already holds it is
    /// usually this app host's own persistent container from an earlier revision: immugw died on
    /// `Bind for 127.0.0.1:3323 failed: port is already allocated`, started without a network, could not
    /// resolve immudb, and took the audit chain and both projects down with it. Every consumer of these
    /// endpoints reads them through an endpoint reference or container DNS, so nothing needs the number
    /// to be predictable.
    /// </remarks>
    [Fact]
    public void Should_NotPinAnyHostPort()
    {
        var offenders = new List<string>();

        foreach (var (file, call) in ResourceCommandSyntax.CallsEverywhere("WithHostPort"))
        {
            offenders.Add($"{file} -> {call.Code()}");
        }

        foreach (var (file, call) in ResourceCommandSyntax.CallsEverywhere("WithEndpoint")
            .Concat(ResourceCommandSyntax.CallsEverywhere("WithHttpEndpoint"))
            .Concat(ResourceCommandSyntax.CallsEverywhere("WithHttpsEndpoint")))
        {
            if (HostPortOf(call) is { } port)
            {
                offenders.Add($"{file} -> {call.Code()} pins host port {port}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    /// <summary>
    /// The <c>port</c> argument of an endpoint call: named, or first positional.
    /// </summary>
    private static string? HostPortOf(InvocationExpressionSyntax call)
    {
        var arguments = call.ArgumentList.Arguments;

        var named = arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == "port");

        if (named is not null)
        {
            return named.Expression.ToString() == "null" ? null : named.Expression.ToString();
        }

        return arguments.Count > 0 && arguments[0].NameColon is null && arguments[0].Expression is LiteralExpressionSyntax
            ? arguments[0].Expression.ToString()
            : null;
    }

    /// <summary>
    /// The E2E fixture passes --Parameters:EphemeralContainers=true to get a Session-lifetime SQL
    /// container. The block keeps that switch; only the *source* decision moves to run time.
    /// </summary>
    [Fact]
    public void Should_KeepTheEphemeralContainersLifetimeSwitch()
    {
        var program = AppHostSource.Program;

        Assert.Contains(
            program.Calls("GetValue"),
            call => call.ArgumentList.Arguments.Any(argument =>
                AppHostSyntax.StringValue(argument.Expression) == "Parameters:EphemeralContainers"));

        var text = AppHostSource.ProgramText;
        Assert.Contains("ContainerLifetime.Session", text, StringComparison.Ordinal);
        Assert.Contains("ContainerLifetime.Persistent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheUserOrgDBContextDatabaseOnTheSqlContainer()
    {
        var root = AppHostSource.Program;
        var database = root.Call("AddDatabase", "UserOrgDBContext");

        Assert.Equal("sql", database.Receiver());
        Assert.True(database.AppliesToSameResource(root, "WithParentRelationship"), "the database lost WithParentRelationship");
    }

    /// <summary>
    /// Program.cs:418-424 — WaitFor only waits for the migration executable to *start*; the gateway's
    /// ForgeMigrationStartupService throws on pending migrations, so both projects must wait for
    /// completion. The block reorders when the SQL chain starts; it must not weaken these edges.
    /// </summary>
    [Fact]
    public void Should_WaitForMigrationsToCompleteBeforeApiAndGateway()
    {
        var program = AppHostSource.Program;
        var waitForCompletion = program.Calls("WaitForCompletion")
            .Where(call => call.ArgumentList.Arguments.ToString().Contains("migration", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Contains(waitForCompletion, call => call.Receiver() == "api");
        Assert.Contains(waitForCompletion, call => call.Receiver() == "gateway");

        Assert.DoesNotContain(
            program.Calls("WaitFor"),
            call => call.ArgumentList.Arguments.ToString().Contains("migration", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// T-085 F1: the API must not wait for the gateway. Nothing the API needs at boot comes from there -
    /// `GatewayInternal__Authority` only feeds `JwtBearerOptions.Authority`, which is fetched lazily at the
    /// first token validation - while the gateway registers an ASP.NET health check that calls the API
    /// (`UserOrgApiHealthCheck`, failureStatus Unhealthy). The dependency runs gateway -> api.
    /// </summary>
    [Fact]
    public void Should_NotMakeTheApiWaitForTheGateway()
    {
        var program = AppHostSource.Program;

        var inverted = program.Calls("WaitFor")
            .Where(call => call.Receiver() == "api")
            .Where(call => call.ArgumentList.Code().MentionsWord("gateway"))
            .ToList();

        Assert.True(
            inverted.Count == 0,
            $"`api.WaitFor(gateway)` is backwards. The gateway's health check calls the API (UserOrg.Gateway/Program.cs, UserOrgApiHealthCheck, failureStatus: Unhealthy), so the dependency runs gateway -> api; the API needs nothing from the gateway at boot, because GatewayInternal__Authority only feeds JwtBearerOptions.Authority and that is fetched at the first token validation. With this edge in place, adding the idiomatic gateway.WithHttpHealthCheck(\"/health\") deadlocks the graph permanently. Found at: {string.Join("; ", inverted.Select(call => call.Describe()))}");

        // With that edge gone, WaitForCompletion(migrationApply) is the API's only remaining ordering guard.
        Assert.Contains(
            program.Calls("WaitForCompletion").Where(call => call.Receiver() == "api"),
            call => call.ArgumentList.Code().MentionsWord("migrationApply"));
    }

    [Fact]
    public void Should_KeepTheMigrationExecutableArgumentShapes()
    {
        Assert.Equal(
            ["apply"],
            MigrationProjectResourceSyntax.Registration("db-migrations-apply").ArgumentShape());

        Assert.Equal(
            ["script", "--output", "script.sql"],
            MigrationProjectResourceSyntax.Registration("db-migrations-script").ArgumentShape());
    }

    [Fact]
    public void Should_WireEveryConnectionStringOnTheMigrationApplyExecutable()
    {
        var apply = MigrationProjectResourceSyntax.Registration("db-migrations-apply");
        var environment = apply.Chain().Calls("WithEnvironment")
            .Select(call => AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression))
            .ToList();

        Assert.Contains("ConnectionStrings__UserOrgDBContext", environment);
        Assert.Contains("ConnectionStrings__ForgeDBContext", environment);
        Assert.Contains("ConnectionStrings__WorkflowDBContext", environment);

        var root = AppHostSource.MigrationToolExtensions;
        Assert.True(apply.AppliesToSameResource(root, "WaitFor"), "db-migrations-apply lost WaitFor(sql)");
        Assert.True(apply.AppliesToSameResource(root, "WithParentRelationship"), "db-migrations-apply lost WithParentRelationship(sql)");
    }

    /// <summary>db-migrations-script is already explicit-start; the block keeps it that way.</summary>
    [Fact]
    public void Should_KeepTheMigrationScriptExecutableExplicitStart()
    {
        var root = AppHostSource.MigrationToolExtensions;
        var script = MigrationProjectResourceSyntax.Registration("db-migrations-script");

        Assert.True(script.AppliesToSameResource(root, "WithExplicitStart"), "db-migrations-script lost WithExplicitStart");
    }

    /// <summary>
    /// Plan step 2: UserorgDev stays the external connection-string secret and DatabasePassword the
    /// container's SA password. Only the switches that gate them are collapsed.
    /// </summary>
    [Fact]
    public void Should_KeepTheDatabaseParameterNamesAndSecrecy()
    {
        var root = AppHostSource.ParameterExtensions;

        var userorgDev = root.Call("AddParameter", "UserorgDev");
        Assert.Contains("secret: true", userorgDev.ArgumentList.Arguments.ToString(), StringComparison.Ordinal);

        var databasePassword = root.Call("AddParameter", "DatabasePassword");
        Assert.Contains("secret: true", databasePassword.ArgumentList.Arguments.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// There is one VaultCredentials behaviour, not two: no deployment of this app host reaches a remote
    /// Vault, so the address is the local proxy whatever the database source turns out to be.
    /// </summary>
    [Fact]
    public void Should_AlwaysTakeVaultCredentialsFromTheLocalProxy()
    {
        var text = AppHostSource.ProgramText;

        Assert.DoesNotContain("RemoteVaultAddress", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "https://hashivault.dev.lexis.it4i.cz",
            AppHostSource.TextOf("IDistributedApplicationBuilderExtensions.cs"),
            StringComparison.Ordinal);
        Assert.Contains("VaultCredentials__MountPoint", text, StringComparison.Ordinal);
        Assert.Contains("vaultProxy.GetEndpoint(\"http\")", text, StringComparison.Ordinal);
    }

    internal static MethodDeclarationSyntax AddParametersDeclaration() =>
        Assert.Single(
            AppHostSource.ParameterExtensions.DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == "AddParameters");
}
