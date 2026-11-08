namespace UserOrg.AppHost.Tests;

public sealed class ExternalServiceSyntaxSelfTests
{
    private const string FakeProgram = """
        var parameters = builder.AddParameters();

        var waldurApi = builder.AddExternalService("waldur", parameters.WaldurBaseUrl)
            .WithHttpHealthCheck("/api/", 401);

        var remoteVault = builder.AddExternalService("remote-vault", parameters.RemoteVaultAddress)
            .WithHttpHealthCheck("/v1/sys/health");

        var remoteDatabase = builder.AddExternalService("remote-database", parameters.UserorgDev);

        var cyclopsApi = builder.AddExternalService("cyclops", "https://vm1.stage.cyclops.lexis.it4i.cz");

        api.WithWaldurEnvironment(parameters, waldurApi);

        api.WithEnvironment(async context =>
        {
            var useLocalContainer = await databaseSource.UseLocalContainerAsync(context.CancellationToken);

            context.EnvironmentVariables["VaultCredentials__Address"] = useLocalContainer
                ? vaultProxy.GetEndpoint("http")
                : remoteVault.Resource.UrlParameter!;
        });

        api.WaitFor(waldurApi);
        """;

    private const string FakeExtensions = """
        namespace Fake;

        internal static class ProjectConfigurationExtensions
        {
            internal static IResourceBuilder<ProjectResource> WithWaldurEnvironment(
                this IResourceBuilder<ProjectResource> api,
                Parameters p,
                IResourceBuilder<ExternalServiceResource> waldur)
            {
                return api.WithEnvironment("Waldur__Instances__0__BaseUrl", waldur);
            }
        }
        """;

    private static readonly ExternalServiceSyntax.Model Fake = ExternalServiceSyntax.ReadSources(
        new Dictionary<string, string>
        {
            ["Fake/Program.cs"] = FakeProgram,
            ["Fake/ProjectConfigurationExtensions.cs"] = FakeExtensions
        });

    [Fact]
    public void Should_FindEveryExternalServiceRegistration()
    {
        Assert.Equal(4, Fake.Registrations.Count);

        Assert.Equal(
            ["cyclops", "remote-database", "remote-vault", "waldur"],
            Fake.Registrations.Select(registration => registration.Name ?? string.Empty).Order().ToArray());
    }

    [Fact]
    public void Should_FindARegistrationByTheParameterItReadsItsUrlFrom()
    {
        var waldur = Fake.ForUrlFrom("Waldur", "WaldurBaseUrl");

        Assert.Equal("waldur", waldur.Name);
        Assert.Equal("waldurApi", waldur.Variable);
    }

    [Fact]
    public void Should_FindARegistrationByItsName()
    {
        var database = Fake.ForNameContaining("the remote database host", "database", "sql", "db");

        Assert.Equal("remote-database", database.Name);
        Assert.True(database.UrlCode.MentionsWord("UserorgDev"));
    }

    [Fact]
    public void Should_FindTheHealthCheckOfARegistration()
    {
        Assert.Contains("/v1/sys/health", Fake.ForUrlFrom("the remote Vault", "RemoteVaultAddress").HealthCheckCode, StringComparison.Ordinal);
        Assert.Contains("/api/", Fake.ForUrlFrom("Waldur", "WaldurBaseUrl").HealthCheckCode, StringComparison.Ordinal);
        Assert.Empty(Fake.ForNameContaining("the remote database host", "database").HealthChecks);
    }

    [Fact]
    public void Should_FindADuplicatedUrlLiteralOnARegistration()
    {
        var cyclops = Fake.ForNameContaining("Cyclops", "cyclops");

        Assert.Contains(
            cyclops.UrlLiterals,
            literal => literal.Contains("://", StringComparison.Ordinal));

        Assert.Empty(Fake.ForUrlFrom("Waldur", "WaldurBaseUrl").UrlLiterals);
    }

    [Fact]
    public void Should_RecogniseAnExternalServiceBuilderPassedIntoAnotherFile()
    {
        var assignment = Fake.SingleEnvironment("Waldur__Instances__0__BaseUrl");

        Assert.Equal("Fake/ProjectConfigurationExtensions.cs", assignment.File);
        Assert.True(
            Fake.ReferencesAnExternalService(assignment.ValueCode),
            "the detector does not recognise an environment variable wired from an `IResourceBuilder<ExternalServiceResource>` method parameter, so the red tests could never go green");
    }

    [Fact]
    public void Should_RecogniseAnExternalServiceInsideAResourceStartCallback()
    {
        var assignment = Fake.SingleEnvironment("VaultCredentials__Address");

        Assert.True(assignment.IsInsideCallback);
        Assert.True(assignment.LocalDatabaseArm.Code().MentionsWord("vaultProxy"));
        Assert.True(Fake.ReferencesAnExternalService(assignment.RemoteDatabaseArm.Code()));
        Assert.False(Fake.ReferencesAnExternalService(assignment.LocalDatabaseArm.Code()));
    }

    [Fact]
    public void Should_FindAStartupGateOnAnExternalService()
    {
        var waldur = Fake.ForUrlFrom("Waldur", "WaldurBaseUrl");
        var gates = Fake.GatesOn(waldur);

        Assert.Single(gates);
        Assert.Equal("WaitFor", gates[0].Method);

        Assert.Empty(Fake.GatesOn(Fake.ForUrlFrom("the remote Vault", "RemoteVaultAddress")));
    }
}
