using System.Net;
using System.Text;

using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests.AuthMatrix;

/// <summary>
/// Pins the provider-write tenant rule (ADR-0105) end to end for the one identity the auth matrix does not
/// model: an interactive ProviderAdmin. <see cref="AuthMatrixTests"/> covers the five role tiers against the
/// Gateway RBAC prediction, but no tier there carries a <c>provider_name</c> claim, so the half of the rule
/// that compares that claim to the route — the half that closed the cross-tenant write — has no live cover.
///
/// Every case addresses the seeded provider by name and leaves the remaining leaf segments absent, so a call
/// that clears authorization dies on the missing child (404) or on validation (400) before it mutates
/// anything: each endpoint authorizes the RAW provider name ahead of parsing, which is what makes an absent
/// leaf safe here.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class ProviderAdminWriteScopeTests(GatewayAttachFixture fixture)
{
    private const string AbsentLeaf = "provideradmin-absent";
    private const string SeededProjectShortName = "TESTPRJ";

    /// <summary>
    /// Every v2 write that gates on <c>ProviderWriteAuthorization</c>, as a template carrying
    /// <c>{providerName}</c>. <c>POST /api/v2/Providers</c> is deliberately absent: creating a provider is
    /// Admin-only by its own check, not by the tenant rule (ADR-0011), and
    /// <see cref="Should_Deny_When_ProviderAdminCreatesAProvider"/> pins that separately.
    /// </summary>
    public static TheoryData<string, string> ProviderWriteRoutes =>
        new()
        {
            { "PUT", "/api/v2/Providers/{providerName}" },
            { "DELETE", "/api/v2/Providers/{providerName}" },
            { "POST", "/api/v2/Providers/{providerName}/Reactivate" },
            { "POST", "/api/v2/Providers/{providerName}/Locations" },
            { "PUT", "/api/v2/Providers/{providerName}/Locations/{locationName}" },
            { "DELETE", "/api/v2/Providers/{providerName}/Locations/{locationName}" },
            { "POST", "/api/v2/Providers/{providerName}/Locations/{locationName}/Assignments" },
            { "PUT", "/api/v2/Providers/{providerName}/Locations/{locationName}/Assignments/{slug}" },
            { "DELETE", "/api/v2/Providers/{providerName}/Locations/{locationName}/Assignments/{slug}" },
            { "PUT", "/api/v2/Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}" },
            { "DELETE", "/api/v2/Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}" }
        };

    [Theory]
    [MemberData(nameof(ProviderWriteRoutes))]
    public async Task Should_AuthorizeWrite_When_ProviderAdminAddressesItsOwnProvider(string httpMethod, string template)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateProviderAdminGatewayClient();
        var path = Substitute(template, GatewayAttachFixture.OwnedProviderName);

        using var request = BuildRequest(httpMethod, path);
        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct);

        // Heal before asserting: DELETE /providers/{name} carries no body to fail and no leaf to miss, so an
        // authorized call really deactivates the seeded provider and would leak into every later case.
        // Reactivate through the API rather than ResetToSeedAsync — a RESTORE DATABASE needs every other
        // connection to the database closed, which does not hold while the topology is serving.
        if (status is >= 200 and < 300)
        {
            using var reactivate = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri($"/api/v2/Providers/{GatewayAttachFixture.OwnedProviderName}/Reactivate", UriKind.Relative));
            using var healed = await client.SendAsync(reactivate, ct);
            healed.EnsureSuccessStatusCode();
        }

        Assert.True(
            status is >= 200 and < 500 and not 401 and not 403,
            $"{httpMethod} {path} as ProviderAdmin: expected an authorized outcome, got {status}.{Environment.NewLine}{body}");
    }

    [Theory]
    [MemberData(nameof(ProviderWriteRoutes))]
    public async Task Should_Deny_When_ProviderAdminAddressesAnotherProvider(string httpMethod, string template)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateProviderAdminGatewayClient();
        var path = Substitute(template, GatewayAttachFixture.ForeignProviderName);

        using var request = BuildRequest(httpMethod, path);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var context = $"{httpMethod} {path} as ProviderAdmin: expected 403, got {(int)response.StatusCode}.{Environment.NewLine}{body}";
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("provider.write_forbidden", body, StringComparison.Ordinal);
        Assert.True(body.Length > 0, context);
    }

    /// <summary>
    /// Creating a provider stays out of the tenant rule: the Gateway refuses a ProviderAdmin on
    /// <c>perm:provider:create</c> before the request reaches Core, so the 403 carries the Gateway's
    /// "Insufficient permissions" body rather than Core's <c>provider.write_forbidden</c>. The endpoint's
    /// own Admin-only check behind it is pinned by its unit tests; this asserts the reach a live
    /// ProviderAdmin actually has.
    /// </summary>
    [Fact]
    public async Task Should_Deny_When_ProviderAdminCreatesAProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateProviderAdminGatewayClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v2/Providers", UriKind.Relative))
        {
            Content = new StringContent(
                """{"name":"provideradmin-created","countryCode":"BD","isPrivate":false}""",
                Encoding.UTF8,
                "application/json")
        };
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"POST /api/v2/Providers as ProviderAdmin: expected 403, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
    }

    private static HttpRequestMessage BuildRequest(string httpMethod, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(httpMethod), new Uri(path, UriKind.Relative));

        if (httpMethod is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string Substitute(string template, string providerName) => template
        .Replace("{providerName}", providerName, StringComparison.Ordinal)
        .Replace("{projectShortName}", SeededProjectShortName, StringComparison.Ordinal)
        .Replace("{locationName}", AbsentLeaf, StringComparison.Ordinal)
        .Replace("{resourceName}", AbsentLeaf, StringComparison.Ordinal)
        .Replace("{slug}", AbsentLeaf, StringComparison.Ordinal);
}