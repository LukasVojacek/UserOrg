using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// The Scalar page of a local run logs in through the realm's public PKCE client scalar, so no client secret
/// reaches the browser. The realm
/// accepts the gateway's and the API's per-run origins through placeholders Keycloak resolves from the container's
/// environment, and the API is told where to log in only on the local-Keycloak branch.
/// </summary>
public sealed class ScalarOidcLoginWiringTests
{
    private const string RealmPath = "externalServices/keycloakRealm/master-realm.json";

    private const string GatewayOriginPlaceholder = "${USERORG_GATEWAY_ORIGIN:https://localhost:5800}";

    private const string ApiOriginPlaceholder = "${USERORG_API_ORIGIN:https://localhost:5500}";

    private const string GatewayDashboardOriginPlaceholder = "${USERORG_GATEWAY_ORIGIN_DASHBOARD:https://localhost:5800}";

    private const string ApiDashboardOriginPlaceholder = "${USERORG_API_ORIGIN_DASHBOARD:https://localhost:5500}";

    private const string ScalarOidcPrefix = "Scalar__Oidc__";

    [Theory]
    [InlineData(GatewayOriginPlaceholder)]
    [InlineData(ApiOriginPlaceholder)]
    [InlineData(GatewayDashboardOriginPlaceholder)]
    [InlineData(ApiDashboardOriginPlaceholder)]
    public void ShouldRegisterTheScalarOriginAsScalarClientRedirectUriAndWebOrigin(string originPlaceholder)
    {
        using var realm = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppHostSource.Directory, RealmPath)));

        var scalarClient = Elements(realm.RootElement.GetProperty("clients"))
            .Single(client => client.GetProperty("clientId").GetString() == "scalar");

        Assert.Contains($"{originPlaceholder}/*", Strings(scalarClient.GetProperty("redirectUris")));
        Assert.Contains(originPlaceholder, Strings(scalarClient.GetProperty("webOrigins")));

        Assert.True(scalarClient.GetProperty("publicClient").GetBoolean());
        Assert.False(scalarClient.TryGetProperty("secret", out _));
        Assert.True(scalarClient.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.False(scalarClient.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.False(scalarClient.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.Equal(
            "S256",
            scalarClient.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
    }

    [Theory]
    [InlineData("GatewayOriginVariable", "USERORG_GATEWAY_ORIGIN", "gateway")]
    [InlineData("ApiOriginVariable", "USERORG_API_ORIGIN", "api")]
    public void ShouldHandTheScalarOriginToKeycloakUnderThePlaceholderName(string constant, string variable, string project)
    {
        var keycloak = AppHostSource.RootOf("KeycloakExtensions.cs");

        Assert.Equal(variable, ConstantValue(keycloak, constant));

        var withClientOrigin = keycloak.Method("WithClientOrigin");

        Assert.True(
            withClientOrigin.Calls("GetEndpoint", "https").Count == 1,
            $"WithClientOrigin no longer reads the project's https endpoint: {withClientOrigin.Describe()}");

        Assert.True(
            withClientOrigin.Calls("GetValueAsync").Count == 1,
            $"WithClientOrigin no longer hands Keycloak the resolved origin string: {withClientOrigin.Describe()}");

        Assert.Equal("_DASHBOARD", ConstantValue(keycloak, "DashboardOriginSuffix"));
        Assert.True(
            withClientOrigin.Code().MentionsWord("DashboardOriginSuffix")
                && withClientOrigin.Code().MentionsWord("DashboardHostOf")
                && withClientOrigin.Code().Contains("ASPNETCORE_URLS", StringComparison.Ordinal),
            $"WithClientOrigin no longer hands Keycloak the dashboard's *.dev.localhost origin: {withClientOrigin.Describe()}");

        var call = Assert.Single(
            AppHostSource.Program.Calls("WithClientOrigin"),
            invocation => invocation.ArgumentList.Arguments[0].Expression.ToString() == $"KeycloakExtensions.{constant}");

        Assert.Equal("keycloak", call.Receiver());
        Assert.Equal(project, call.ArgumentList.Arguments[1].Expression.ToString());
    }

    [Fact]
    public void ShouldConfigureScalarOidcOnlyOnTheLocalKeycloakBranch()
    {
        var assignments = ExternalServiceSyntax.AppHost.EnvironmentAssignments
            .Where(assignment => assignment.Name.StartsWith(ScalarOidcPrefix, StringComparison.Ordinal))
            .ToList();

        var lambdas = assignments
            .Select(assignment => assignment.Node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().LastOrDefault())
            .Distinct()
            .ToList();

        Assert.True(
            lambdas.Count == 1 && lambdas[0] is not null,
            $"expected the `{ScalarOidcPrefix}*` keys in exactly one environment callback, found {lambdas.Count}");

        var lambda = lambdas[0]!;
        var registration = lambda.Ancestors().OfType<InvocationExpressionSyntax>().First();

        Assert.Equal("WithEnvironment", AppHostSyntax.NameOf(registration));
        Assert.Equal("api", registration.Receiver());
        Assert.True(
            lambda.Calls("UseLocalKeycloakAsync").Count == 1,
            $"the Scalar OIDC callback no longer asks whether the run uses the local Keycloak: {lambda.Describe()}");

        Assert.Equal(
            ["AuthorizationUrl", "ClientId", "TokenUrl"],
            assignments.Select(assignment => assignment.Name[ScalarOidcPrefix.Length..]).Order(StringComparer.Ordinal));

        Assert.True(
            SourceOf(Single(assignments, "AuthorizationUrl")).MentionsWord("RealmAuthorizationUrl"),
            "Scalar__Oidc__AuthorizationUrl no longer comes from keycloak.RealmAuthorizationUrl()");
        Assert.True(
            SourceOf(Single(assignments, "TokenUrl")).MentionsWord("RealmTokenUrl"),
            "Scalar__Oidc__TokenUrl no longer comes from keycloak.RealmTokenUrl()");
        Assert.True(
            Single(assignments, "ClientId").ValueCode.MentionsWord("ScalarClientId"),
            "Scalar__Oidc__ClientId no longer comes from KeycloakExtensions.ScalarClientId");

        var keycloak = AppHostSource.RootOf("KeycloakExtensions.cs");
        Assert.Contains(
            "/realms/master/protocol/openid-connect/auth",
            keycloak.Method("RealmAuthorizationUrl").Code(),
            StringComparison.Ordinal);
        Assert.Contains(
            "/realms/master/protocol/openid-connect/token",
            keycloak.Method("RealmTokenUrl").Code(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Scalar__Oidc",
            AppHostSource.TextOf("ProjectConfigurationExtensions.cs"),
            StringComparison.Ordinal);
    }

    private static List<JsonElement> Elements(JsonElement array) =>
        [.. Enumerable.Range(0, array.GetArrayLength()).Select(index => array[index])];

    private static List<string?> Strings(JsonElement array) =>
        [.. Elements(array).Select(element => element.GetString())];

    private static IReadOnlyList<string> StringLiterals(SyntaxNode root) =>
        [.. root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText)];

    private static string? ConstantValue(SyntaxNode root, string name)
    {
        var declarator = root.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword))
            .SelectMany(field => field.Declaration.Variables)
            .SingleOrDefault(variable => variable.Identifier.ValueText == name);

        Assert.True(declarator is not null, $"KeycloakExtensions declares no `{name}` constant");

        return declarator.Initializer is { } initializer ? AppHostSyntax.StringValue(initializer.Value) : null;
    }

    private static ExternalServiceSyntax.EnvironmentAssignment Single(
        IEnumerable<ExternalServiceSyntax.EnvironmentAssignment> assignments,
        string name,
        string prefix = ScalarOidcPrefix)
    {
        var matches = assignments.Where(assignment => assignment.Name == prefix + name).ToList();

        Assert.True(matches.Count == 1, $"expected exactly one `{prefix}{name}` assignment, found {matches.Count}");

        return matches[0];
    }

    /// <summary>
    /// The code an environment value comes from: the initializer of the local it names, or the value itself.
    /// </summary>
    private static string SourceOf(ExternalServiceSyntax.EnvironmentAssignment assignment)
    {
        if (assignment.Value is not IdentifierNameSyntax local)
        {
            return assignment.ValueCode;
        }

        var declarator = assignment.Node.SyntaxTree.GetRoot().DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .SingleOrDefault(variable => variable.Identifier.ValueText == local.Identifier.ValueText);

        return declarator?.Initializer?.Value.Code() ?? assignment.ValueCode;
    }

    private static string FormValue(SyntaxNode method, string field)
    {
        var entry = method.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .SingleOrDefault(assignment => assignment.Left is ImplicitElementAccessSyntax access
                && access.ArgumentList.Arguments.Count == 1
                && AppHostSyntax.StringValue(access.ArgumentList.Arguments[0].Expression) == field);

        Assert.True(entry is not null, $"gettoken posts no `{field}` form field");

        return entry.Right.Code();
    }
}
