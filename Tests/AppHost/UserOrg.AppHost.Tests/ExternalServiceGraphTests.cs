namespace UserOrg.AppHost.Tests;

public sealed class ExternalServiceGraphTests
{
    private const string IdpSubject = "the identity provider authority";
    private const string WaldurSubject = "Waldur";
    private const string CyclopsSubject = "Cyclops";
    private const string RemoteDatabaseSubject = "the remote database host";

    private static ExternalServiceSyntax.Registration Idp() =>
        ExternalServiceSyntax.AppHost.ForUrlFrom(IdpSubject, "IdpAuthority", "idpAuthority", "Authority");

    private static ExternalServiceSyntax.Registration Waldur() =>
        ExternalServiceSyntax.AppHost.ForUrlFrom(WaldurSubject, "WaldurBaseUrl", "waldurBaseUrl");

    private static ExternalServiceSyntax.Registration Cyclops() =>
        ExternalServiceSyntax.AppHost.ForUrlFrom(CyclopsSubject, "CyclopsBaseUrl", "cyclopsBaseUrl");

    private static ExternalServiceSyntax.Registration RemoteDatabase() =>
        ExternalServiceSyntax.AppHost.ForNameContaining(RemoteDatabaseSubject, "database", "sql", "db");

    [Fact]
    public void Should_RegisterTheIdentityProviderAuthorityAsAnExternalService() => Assert.NotNull(Idp());

    [Fact]
    public void Should_RegisterWaldurAsAnExternalService() => Assert.NotNull(Waldur());

    [Fact]
    public void Should_RegisterCyclopsAsAnExternalService() => Assert.NotNull(Cyclops());

    [Fact]
    public void Should_RegisterTheRemoteDatabaseHostAsAnExternalService() => Assert.NotNull(RemoteDatabase());

    [Fact]
    public void Should_RegisterEveryExternalDependencyExactlyOnce()
    {
        Assert.True(
            ExternalServiceSyntax.AppHost.Registrations.Count >= 4,
            $"expected the external dependencies (the identity provider, Waldur, Cyclops, the remote Vault and the remote database host) to be registered with `AddExternalService`, but {ExternalServiceSyntax.AppHost.Inventory}");

        var names = ExternalServiceSyntax.AppHost.Registrations.Select(registration => registration.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Should_FeedEveryExternalServiceFromAParameterRatherThanADuplicatedUrl()
    {
        Assert.True(
            ExternalServiceSyntax.AppHost.Registrations.Count >= 4,
            $"no external service to check: {ExternalServiceSyntax.AppHost.Inventory}");

        foreach (var registration in ExternalServiceSyntax.AppHost.Registrations)
        {
            var literals = registration.UrlLiterals
                .Where(literal => literal.Contains("://", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                literals.Count == 0,
                $"{registration.Label} duplicates a URL instead of reading the parameter that already carries it: "
                    + string.Join(", ", literals));
        }
    }

    [Fact]
    public void Should_ProbeTheIdentityProviderWithItsOpenIdConfigurationDocument()
    {
        var idp = Idp();

        Assert.True(
            idp.HealthChecks.Count > 0,
            $"{idp.Label} has no `WithHttpHealthCheck`; the identity provider serves a discovery document that makes a usable probe, and without it the dashboard cannot show its health state");

        Assert.Contains(
            "well-known/openid-configuration",
            idp.HealthCheckCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Should_WireTheWaldurBaseUrlFromTheExternalServiceResource()
    {
        var waldur = Waldur();
        var assignment = ExternalServiceSyntax.AppHost.SingleEnvironment("Waldur__Instances__0__BaseUrl");

        Assert.True(
            ExternalServiceSyntax.AppHost.ReferencesAnExternalService(assignment.ValueCode),
            $"`Waldur__Instances__0__BaseUrl` still reads the parameter directly instead of {waldur.Label}: "
                + assignment.ValueCode);
    }

    [Fact]
    public void Should_WireTheCyclopsBaseUrlFromTheExternalServiceResource()
    {
        var cyclops = Cyclops();
        var assignment = ExternalServiceSyntax.AppHost.SingleEnvironment("Cyclops__BaseUrl");

        Assert.True(
            ExternalServiceSyntax.AppHost.ReferencesAnExternalService(assignment.ValueCode),
            $"`Cyclops__BaseUrl` still reads the parameter directly instead of {cyclops.Label}: "
                + assignment.ValueCode);
    }

    [Theory]
    [InlineData("Idp__Authority")]
    [InlineData("AuthenticationConfiguration__MetadataAddress")]
    public void Should_WireTheIdentityProviderAuthorityFromTheExternalServiceResource(string name)
    {
        var idp = Idp();

        var fromTheExternalIdp = ExternalServiceSyntax.AppHost.Environment(name)
            .Where(assignment => !assignment.ValueCode.Contains("eycloak", StringComparison.Ordinal))
            .ToList();

        Assert.True(fromTheExternalIdp.Count > 0, $"no external-identity-provider `{name}` assignment left to check");

        foreach (var assignment in fromTheExternalIdp)
        {
            // MetadataAddress builds the discovery document inside a resource-start callback, so the
            // external-service reference sits in the enclosing lambda rather than the assignment.
            var code = assignment.IsInsideCallback ? assignment.ScopeCode : assignment.ValueCode;

            Assert.True(
                ExternalServiceSyntax.AppHost.ReferencesAnExternalService(code),
                $"`{name}` still reads the authority parameter directly instead of {idp.Label}: {assignment.Label} = {assignment.ValueCode}");
        }
    }

    [Fact]
    public void Should_WireTheRemoteDatabaseHostFromTheSameSourceAsTheConnectionString()
    {
        var database = RemoteDatabase();

        Assert.True(
            database.UrlCode.MentionsWord("UserorgDev")
                || database.UrlCode.MentionsWord("RemoteDatabaseHost")
                || database.UrlCode.MentionsWord("remoteDatabaseHost"),
            $"{database.Label} takes its URL from neither the UserorgDev connection string nor a dedicated remote-database-host parameter, so the graph would show a host nothing else uses: {database.UrlCode}");
    }

    [Theory]
    [InlineData(IdpSubject)]
    [InlineData(WaldurSubject)]
    [InlineData(CyclopsSubject)]
    public void Should_NotGateStartupOnANonEssentialExternalService(string subject)
    {
        var registration = subject switch
        {
            IdpSubject => Idp(),
            WaldurSubject => Waldur(),
            _ => Cyclops()
        };

        var gates = ExternalServiceSyntax.AppHost.GatesOn(registration);

        Assert.True(
            gates.Count == 0,
            $"{registration.Label} is waited on, which turns a dependency that used to be nothing but an environment URL into a startup gate; visibility in the graph is the goal, not a new gate: {string.Join("; ", gates.Select(gate => gate.Label))}");
    }

    /// <summary>
    /// The parameter behind an external service must carry a value of its own. Aspire resolves it from
    /// an InitializeResourceEvent handler that DcpExecutor awaits before it creates any container or
    /// executable - the dashboard included - so a parameter left to the unresolved-parameter dialog
    /// hangs the entire run at "Starting dashboard..." with nothing to answer the dialog on.
    /// </summary>
    [Fact]
    public void Should_GiveEveryExternalServiceUrlParameterAValueOfItsOwn()
    {
        var registrations = ExternalServiceSyntax.AppHost.Registrations;

        Assert.True(registrations.Count >= 4, $"no external service to check: {ExternalServiceSyntax.AppHost.Inventory}");

        foreach (var registration in registrations)
        {
            var parameter = registration.UrlCode.Trim();

            if (parameter.Length == 0)
            {
                continue;
            }

            var declaration = ExternalServiceSyntax.AppHost.DeclarationOf(parameter);

            Assert.True(
                declaration is null || DeclaresAValue(declaration),
                $"{registration.Label} takes its URL from `{parameter}`, which has no ParameterDefault. Aspire resolves it before the dashboard exists, so the run hangs at \"Starting dashboard...\" with no dialog to answer: {declaration}");
        }
    }

    /// <summary>
    /// Same window, different reader: the gateway's WithUrls callback runs during endpoint-URL
    /// publishing, which DcpExecutor also awaits before creating anything.
    /// </summary>
    [Fact]
    public void Should_GiveEveryParameterReadFromAWithUrlsCallbackAValueOfItsOwn()
    {
        var parameters = ExternalServiceSyntax.AppHost.ParametersReadInWithUrlsCallbacks();

        foreach (var parameter in parameters)
        {
            var declaration = ExternalServiceSyntax.AppHost.DeclarationOf(parameter);

            Assert.True(
                declaration is null || DeclaresAValue(declaration),
                $"`{parameter}` is read from a WithUrls callback but has no ParameterDefault; endpoint-URL publishing blocks on it before the dashboard starts: {declaration}");
        }
    }

    private static bool DeclaresAValue(string declaration) =>
        declaration.Contains("RuntimeDefault", StringComparison.Ordinal)
        || declaration.Contains("GenerateParameterDefault", StringComparison.Ordinal);

    [Fact]
    public void Should_NotWaitForAnyExternalServiceAnywhereInTheAppHost()
    {
        Assert.True(
            ExternalServiceSyntax.AppHost.Registrations.Count >= 4,
            $"no external service to check: {ExternalServiceSyntax.AppHost.Inventory}");

        var gates = ExternalServiceSyntax.AppHost.WaitEdges
            .Where(edge => ExternalServiceSyntax.AppHost.ReferencesAnExternalService(edge.ArgumentCode))
            .ToList();

        Assert.True(
            gates.Count == 0,
            $"the AppHost waits on an external service; none of these five dependencies is reachable at model build time and the app must still start when one of them is down: {string.Join("; ", gates.Select(gate => gate.Label))}");
    }

    [Fact]
    public void Should_NotRegisterTheHeappeMockAsAnExternalService()
    {
        var heappe = ExternalServiceSyntax.AppHost.Registrations
            .Where(registration => registration.Call.Chain().Code().Contains("eappe", StringComparison.Ordinal)
                || (registration.Name?.Contains("eappe", StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        Assert.True(
            heappe.Count == 0,
            "the HEAppE mock is a WireMock resource already in the graph and is explicitly out of scope: "
                + string.Join("; ", heappe.Select(registration => registration.Label)));

        Assert.True(
            ResourceCommandSyntax.CallsEverywhere("AddHeappeMock").Count > 0,
            "the HEAppE WireMock registration is gone");
    }
}
