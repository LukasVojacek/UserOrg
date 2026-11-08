namespace UserOrg.AppHost.Tests;

public sealed class ExternalServiceEnvironmentCharacterizationTests
{
    [Fact]
    public void Should_KeepTheWaldurBaseUrlOnTheApiFromTheWaldurBaseUrlParameter()
    {
        var assignment = ExternalServiceSyntax.AppHost.SingleEnvironment("Waldur__Instances__0__BaseUrl");

        Assert.True(
            assignment.ValueCode.MentionsWord("WaldurBaseUrl"),
            $"the Waldur base URL no longer comes from the WaldurBaseUrl parameter: {assignment.Label} = {assignment.ValueCode}");

        Assert.True(
            assignment.Receiver == "api",
            $"the Waldur base URL is no longer set on the API resource: {assignment.Label}");
    }

    [Fact]
    public void Should_KeepTheCyclopsUrlsOnTheGatewayFromTheirOwnParameters()
    {
        var baseUrl = ExternalServiceSyntax.AppHost.SingleEnvironment("Cyclops__BaseUrl");

        Assert.True(
            baseUrl.ValueCode.MentionsWord("CyclopsBaseUrl"),
            $"the Cyclops base URL no longer comes from the CyclopsBaseUrl parameter: {baseUrl.Label} = {baseUrl.ValueCode}");

        Assert.True(
            baseUrl.Receiver == "gateway",
            $"the Cyclops base URL is no longer set on the gateway resource: {baseUrl.Label}");

        var timeUsageRelativeUrl = ExternalServiceSyntax.AppHost.SingleEnvironment("Cyclops__TimeUsageRelativeUrl");
        Assert.True(
            timeUsageRelativeUrl.ValueCode.MentionsWord("CyclopsTimeUsageRelativeUrl"),
            $"the Cyclops relative URL no longer comes from the CyclopsTimeUsageRelativeUrl parameter: {timeUsageRelativeUrl.ValueCode}");


        var totalUsageRelativeUrl = ExternalServiceSyntax.AppHost.SingleEnvironment("Cyclops__TotalUsageRelativeUrl");
        Assert.True(
            totalUsageRelativeUrl.ValueCode.MentionsWord("CyclopsTotalUsageRelativeUrl"),
            $"the Cyclops relative URL no longer comes from the CyclopsTotalUsageRelativeUrl parameter: {totalUsageRelativeUrl.ValueCode}");
    }

    [Theory]
    [InlineData("Idp__Authority")]
    [InlineData("AuthenticationConfiguration__MetadataAddress")]
    public void Should_KeepTheIdpAuthorityEnvironmentsOnBothBranches(string name)
    {
        var assignments = ExternalServiceSyntax.AppHost.Environment(name);

        Assert.True(assignments.Count > 0, $"the AppHost no longer sets the `{name}` environment variable");

        var fromTheLocalKeycloak = assignments
            .Where(assignment => assignment.ValueCode.Contains("eycloak", StringComparison.Ordinal))
            .ToList();

        var fromTheExternalIdp = assignments.Except(fromTheLocalKeycloak).ToList();

        Assert.True(
            fromTheLocalKeycloak.Count > 0,
            $"no `{name}` assignment reads the local Keycloak container any more");

        Assert.True(
            fromTheExternalIdp.Count > 0,
            $"no `{name}` assignment reads the external identity provider any more");

        foreach (var assignment in fromTheExternalIdp)
        {
            // MetadataAddress derives the discovery document from the authority inside a resource-start
            // callback, so the reference lives in the enclosing lambda, not on the assignment itself.
            var code = assignment.IsInsideCallback ? assignment.ScopeCode : assignment.ValueCode;

            Assert.True(
                code.MentionsWord("Authority"),
                $"an external-identity-provider `{name}` assignment no longer carries the IdP authority URL: {assignment.Label} = {assignment.ValueCode}");
        }
    }

    [Fact]
    public void Should_KeepTheIdpAuthorityOnBothTheApiAndTheGateway()
    {
        var receivers = ExternalServiceSyntax.AppHost.Environment("Idp__Authority")
            .Where(assignment => !assignment.ValueCode.Contains("eycloak", StringComparison.Ordinal))
            .Select(assignment => assignment.Receiver)
            .ToList();

        Assert.Contains("api", receivers);
        Assert.Contains("gateway", receivers);
    }

    [Theory]
    [InlineData("Idp__Introspection__Endpoint", "IntrospectionEndpoint")]
    [InlineData("InactiveUserSync__BaseUrl", "InactiveUserSyncBaseUrl")]
    public void Should_KeepTheNeighbouringUrlsOnTheirOwnParameters(string name, string parameter)
    {
        var assignment = ExternalServiceSyntax.AppHost.SingleEnvironment(name);

        Assert.True(
            assignment.ValueCode.MentionsWord(parameter),
            $"`{name}` no longer comes from the {parameter} parameter: {assignment.Label} = {assignment.ValueCode}. It carries the identity provider's host by default but is a separate parameter and must keep its own value.");
    }

    [Fact]
    public void Should_TakeTheVaultAddressFromTheProxyWithoutABranch()
    {
        var assignments = ExternalServiceSyntax.AppHost.Environment("VaultCredentials__Address");

        Assert.True(assignments.Count > 0, "the AppHost no longer sets `VaultCredentials__Address`");

        var assignment = Assert.Single(assignments.Where(candidate => candidate.IsInsideCallback).ToList());

        Assert.True(
            assignment.ValueCode.MentionsWord("vaultProxy"),
            $"`VaultCredentials__Address` no longer points at the VaultProxy endpoint: {assignment.ValueCode}");

        Assert.DoesNotContain("?", assignment.ValueCode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("VaultCredentials__AddressPrefix")]
    [InlineData("VaultCredentials__MountPoint")]
    public void Should_KeepTheVaultCredentialsNeighbours(string name)
    {
        Assert.True(
            ExternalServiceSyntax.AppHost.Environment(name).Count > 0,
            $"the AppHost no longer sets `{name}`");
    }

    [Theory]
    [InlineData("ConnectionStrings__ForgeDBContext")]
    [InlineData("ConnectionStrings__UserOrgDBContext")]
    [InlineData("ConnectionStrings__WorkflowDBContext")]
    public void Should_KeepTheDatabaseConnectionStringsOnTheUserorgDevParameter(string name)
    {
        var assignments = ExternalServiceSyntax.AppHost.Environment(name);

        Assert.True(assignments.Count > 0, $"the AppHost no longer sets `{name}`");

        var onTheProjects = assignments.Where(assignment => assignment.IsInsideCallback).ToList();

        Assert.True(
            onTheProjects.Count >= 2,
            $"expected `{name}` to be resolved inside a resource-start callback on both the API and the gateway, found {onTheProjects.Count}");

        foreach (var assignment in onTheProjects)
        {
            Assert.True(
                assignment.ScopeCode.MentionsWord("UserorgDev"),
                $"the callback that writes `{name}` no longer reads the UserorgDev connection string for the external database: {assignment.Label}");

            Assert.True(
                assignment.ScopeCode.Contains("ConnectionStringReference", StringComparison.Ordinal),
                $"the callback that writes `{name}` no longer reads the local SQL container's connection string: "
                    + assignment.Label);
        }
    }

    [Theory]
    [InlineData("IdpAuthority", "https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV")]
    [InlineData("WaldurBaseUrl", "https://allocations.dev.my-eurohpc.eu")]
    [InlineData("cyclopsBaseUrl", "https://vm1.stage.cyclops.lexis.it4i.cz")]
    public void Should_KeepTheDefaultUrlOfEveryParameterThatFeedsAnExternalDependency(string parameter, string expected)
    {
        var declaration = AppHostSource.ParameterExtensions.Call("AddParameter", parameter);

        Assert.Contains(expected, declaration.Chain().Code(), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotCarryARemoteVaultAddressAnywhereInTheAppHost()
    {
        Assert.DoesNotContain(
            "hashivault.dev.lexis.it4i.cz",
            VaultSyntax.AppHostCode,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IdpAuthority")]
    [InlineData("WaldurBaseUrl")]
    [InlineData("cyclopsBaseUrl")]
    [InlineData("UserorgDev")]
    public void Should_NotGateStartupOnTheParametersThatCarryTheseUrls(string parameter)
    {
        var gates = ExternalServiceSyntax.AppHost.WaitEdges
            .Where(edge => edge.ArgumentCode.MentionsWord(parameter))
            .ToList();

        Assert.True(
            gates.Count == 0,
            $"the AppHost now waits on the {parameter} parameter, which gates startup on a dependency that was only an environment URL before: {string.Join("; ", gates.Select(gate => gate.Label))}");
    }
}
