using Microsoft.CodeAnalysis;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// The dashboard's summary row is reserved for URLs that open a usable UI; every other URL is pushed
/// into the resource's details with <c>WithUrlForEndpoint(name, … DetailsOnly)</c>.
/// </summary>
/// <remarks>
/// That call is silent about a name no endpoint carries: `immugw` hid its REST port behind a
/// `WithUrlForEndpoint("https", …)` while its only endpoint was named `http`, so the URL stayed in the
/// summary and nothing said so. These tests check the names against the endpoints the same chain declares.
/// </remarks>
public sealed class DashboardUrlVisibilityTests
{
    [Fact]
    public void Should_NameAnEndpointTheSameResourceDeclares()
    {
        var checkedNames = new List<string>();
        var offenders = new List<string>();

        foreach (var (file, call) in ResourceCommandSyntax.CallsEverywhere("WithUrlForEndpoint"))
        {
            var declared = DeclaredEndpointNames(call.Chain());

            // Endpoints the hosting package creates (Keycloak, MailPit, SQL Server, WireMock) appear
            // nowhere in this source, so the chain declares none and there is nothing to check against.
            if (declared.Count == 0)
            {
                continue;
            }

            var name = AppHostSyntax.StringValue(call.ArgumentList.Arguments[0].Expression);

            if (name is null)
            {
                continue;
            }

            checkedNames.Add($"{file} -> {name}");

            if (!declared.Contains(name))
            {
                offenders.Add($"{file} -> WithUrlForEndpoint(\"{name}\") on a resource whose endpoints are "
                    + string.Join(", ", declared));
            }
        }

        Assert.True(
            checkedNames.Count > 0,
            "no WithUrlForEndpoint call in the AppHost sits on a chain that declares its own endpoints, so this test would pass vacuously; DeclaredEndpointNames no longer matches the AppHost's code");

        Assert.True(offenders.Count == 0, string.Join("; ", offenders));
    }

    [Fact]
    public void Should_KeepTheApiExplorerAndTheRoleManagementUiInTheSummary()
    {
        var program = ProgramCode();

        foreach (var ui in new[] { "🙏 UserOrg", "🛠️ Forge management" })
        {
            var index = program.IndexOf(ui, StringComparison.Ordinal);

            Assert.True(index >= 0, $"the gateway no longer publishes a `{ui}` URL");

            Assert.Contains(
                "DisplayLocation = UrlDisplayLocation.SummaryAndDetails",
                program[index..(index + 200)],
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Should_GiveEveryUrlAName()
    {
        var anonymous = new List<string>();

        foreach (var (file, call) in ResourceCommandSyntax.CallsEverywhere("WithUrlForEndpoint"))
        {
            if (!call.ArgumentList.Code().Contains("DisplayText", StringComparison.Ordinal))
            {
                anonymous.Add($"{file} -> {call.ArgumentList.Code()}");
            }
        }

        Assert.True(
            anonymous.Count == 0,
            "a dashboard URL is shown under the raw scheme instead of a name a reader recognises: "
                + string.Join("; ", anonymous));
    }

    [Fact]
    public void Should_KeepTheOpenIdConfigurationDocumentOutOfTheSummary()
    {
        var program = ProgramCode();
        var index = program.IndexOf("OpenID configuration", StringComparison.Ordinal);

        Assert.True(index >= 0, "the gateway no longer publishes the .well-known URL");

        Assert.Contains(
            "DisplayLocation = UrlDisplayLocation.DetailsOnly",
            program[index..(index + 200)],
            StringComparison.Ordinal);
    }

    private static string ProgramCode() =>
        ResourceCommandSyntax.AllRoots[Path.Combine("src", "UserOrg.AppHost", "Program.cs")].Code();

    /// <summary>
    /// The endpoint names a fluent chain declares on its own resource. An unnamed endpoint takes its
    /// scheme as the name, which is what <c>WithHttpEndpoint</c> and a bare <c>WithEndpoint</c> rely on.
    /// </summary>
    private static HashSet<string> DeclaredEndpointNames(SyntaxNode chain)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var call in chain.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            var method = AppHostSyntax.NameOf(call);

            var fallback = method switch
            {
                "WithHttpEndpoint" => "http",
                "WithHttpsEndpoint" => "https",
                "WithEndpoint" => Argument(call, "scheme", 2) ?? "tcp",
                _ => null
            };

            if (fallback is null)
            {
                continue;
            }

            names.Add(Argument(call, "name", method == "WithEndpoint" ? 3 : 2) ?? fallback);
        }

        return names;
    }

    private static string? Argument(InvocationExpressionSyntax call, string name, int position)
    {
        var named = call.ArgumentList.Arguments
            .FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == name);

        if (named is not null)
        {
            return AppHostSyntax.StringValue(named.Expression);
        }

        if (call.ArgumentList.Arguments.Any(argument => argument.NameColon is not null))
        {
            return null;
        }

        return call.ArgumentList.Arguments.Count > position
            ? AppHostSyntax.StringValue(call.ArgumentList.Arguments[position].Expression)
            : null;
    }
}
