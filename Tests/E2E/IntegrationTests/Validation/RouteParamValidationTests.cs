using System.Net;
using System.Text.Json;

using E2E.Fixtures;

using ParityTests;

namespace IntegrationTests.Validation;

/// <summary>
/// Malformed-route-param specs (issue #260): DELETE and GET/PUT/POST endpoints whose route binds a
/// natural-key value object (<c>ProviderName</c>/<c>TagName</c>/<c>ResourceName</c>/<c>LocationName</c>/
/// <c>ProjectShortName</c>), a <c>Guid</c>, or a plain <c>[FromRoute] string</c> the handler parses itself,
/// get one malformed-input case: a value that still matches the route template (so routing dispatches to
/// the endpoint) but fails the parameter's <c>TryCreate</c>/<c>TryParse</c> rule. Each case asserts the
/// specific 4xx that validation legitimately produces — never a 500
/// (<see cref="Should_ReturnNonServerError_When_RouteParamIsMalformed"/>).
///
/// This is a narrower set than B04's full route inventory (11 DELETE endpoints + every GET/PUT/POST with a
/// non-trivial route param); see <see cref="MalformedRouteParamData"/>'s remarks for the endpoints skipped
/// as genuinely trivial (opaque tokens, slugs, unvalidated <c>{idpUserId}</c>).
///
/// #260 fixed two root causes, both exercised here (counts below describe those two root causes, not row
/// counts — the table has 27 rows, one dedicated <c>[Fact]</c> below covers a 28th): 16 endpoints bind their
/// value object directly as a typed route parameter (<c>[FromRoute] ProjectShortName</c>,
/// <c>[FromRoute] Guid</c>) and are mapped by <c>GlobalExceptionHandler</c>'s <c>BadHttpRequestException</c>
/// branch; 2 endpoints (<c>GetProviderEndpoint</c>, <c>PromoteToProviderAdminEndpoint</c>) resolve a
/// plain-string route value via <c>ProviderName.TryCreate</c> and return 400 through the v2
/// <c>Problems.cs</c> helper. The two paths agree on <c>status</c>/<c>title</c> and both always carry a
/// non-empty (but not equal — see ADR-2) <c>code</c> — see
/// <see cref="Should_AgreeOnCoreProblemShape_When_ComparingHandlerAndProblemsPaths"/> for what is and is not
/// asserted, and <see cref="MalformedRouteParamData"/>'s remarks for the root cause each path fixes.
///
/// Uses the Admin client throughout (an Allowed-tier identity that clears authorization on every v2 route
/// per <c>AuthMatrixData</c>) so a case can only fail on the input's shape, never on role/membership —
/// the same isolation rule B03 applies to request-body validation. The 401-before-400 precedence is asserted
/// separately, as its own unauthenticated-client test method
/// (<see cref="Should_ReturnUnauthorized_When_CallerIsUnauthenticatedAndRouteParamIsMalformed"/>): a data row
/// here would always be authenticated and could never observe it.
///
/// Every route only reads or rejects before touching state (the malformed segment fails validation ahead
/// of any repository write), so no seed healing is required for the table-driven cases (contrast
/// <c>AuthMatrixData.IsSeedProjectDelete</c>, which guards the one route B02 can genuinely mutate). The one
/// exception is <see cref="Should_ReturnBadRequest_When_PromoteToProviderAdminProviderNameIsMalformed"/>,
/// which targets the seeded project owner as a user record (not a project) and needs that user's raw seed
/// row healed first — see its own comment.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class RouteParamValidationTests(GatewayAttachFixture fixture)
{
    // A whitespace-only route segment: same corruption MalformedRouteParamData uses, kept local here because
    // the tests below build their requests directly rather than through the shared theory data.
    private const string Whitespace = "%20";

    public static TheoryData<string, string, string, int> Cases => MalformedRouteParamData.BuildCases();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Should_ReturnNonServerError_When_RouteParamIsMalformed(
        string name, string httpMethod, string path, int expectedStatus)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();
        using var request = new HttpRequestMessage(new HttpMethod(httpMethod), new Uri(path, UriKind.Relative));

        using var response = await client.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            status is < 500,
            $"{name} ({httpMethod} {path}): malformed route param must never 500. Got {status}.{Environment.NewLine}{body}");
        Assert.True(
            status == expectedStatus,
            $"{name} ({httpMethod} {path}): expected {expectedStatus}, got {status}.{Environment.NewLine}{body}");
    }

    // ADR-0020 requires every v2 failure body to carry a stable code, a human detail, and (on the handler
    // path) the correlation id — and to never leak the raw framework binding message. GetProjectEndpoint
    // (BadHttpRequestException branch, GlobalExceptionHandler.cs) is the handler-path representative.
    [Fact]
    public async Task Should_CarryCodeDetailAndRequestId_When_HandlerPathRouteParamIsMalformed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();

        using var response = await client.GetAsync(new Uri($"/api/v2/Projects/{Whitespace}", UriKind.Relative), ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        using var body = JsonDocument.Parse(raw);
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation.invalid_format", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("requestId").GetString()));
        Assert.DoesNotContain("Failed to bind parameter", raw, StringComparison.Ordinal);
    }

    // Scoped shape parity between the two 400-producing paths (see class remarks): GetProjectEndpoint
    // (handler path) vs. GetProviderEndpoint (Problems.cs path) for the identical whitespace-only-segment
    // failure. `status` and `title` are asserted equal because both paths deliberately share Problems.cs's
    // "Validation failed" title (GlobalExceptionHandler.cs). `detail` is NOT asserted equal: the handler
    // path emits one fixed generic sentence for every BadHttpRequestException, while the Problems.cs path
    // emits ProviderName's own validation message — different by design, not a defect. `code` is asserted
    // present and non-empty on both, AND asserted unequal: `validation.invalid_format` vs.
    // `provider.invalid_name` are semantically different codes for two different validation mechanisms
    // (ADR-2) — pinning the inequality is the documented decision, not an oversight.
    // Content-Type, `type` and `requestId` are pre-existing, documented divergences (see
    // MalformedRouteParamData remarks) and are out of scope here.
    [Fact]
    public async Task Should_AgreeOnCoreProblemShape_When_ComparingHandlerAndProblemsPaths()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAdminGatewayClient();

        using var handlerPathResponse = await client.GetAsync(new Uri($"/api/v2/Projects/{Whitespace}", UriKind.Relative), ct);
        using var handlerPathBody = JsonDocument.Parse(await handlerPathResponse.Content.ReadAsStringAsync(ct));
        var handlerPath = handlerPathBody.RootElement;

        using var problemsPathResponse = await client.GetAsync(new Uri($"/api/v2/Providers/{Whitespace}", UriKind.Relative), ct);
        using var problemsPathBody = JsonDocument.Parse(await problemsPathResponse.Content.ReadAsStringAsync(ct));
        var problemsPath = problemsPathBody.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, handlerPathResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, problemsPathResponse.StatusCode);
        Assert.Equal(handlerPath.GetProperty("status").GetInt32(), problemsPath.GetProperty("status").GetInt32());
        Assert.Equal(handlerPath.GetProperty("title").GetString(), problemsPath.GetProperty("title").GetString());

        var handlerPathCode = handlerPath.GetProperty("code").GetString();
        var problemsPathCode = problemsPath.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(handlerPathCode));
        Assert.False(string.IsNullOrWhiteSpace(problemsPathCode));
        Assert.NotEqual(handlerPathCode, problemsPathCode);
    }

    // PromoteToProviderAdminEndpoint's route validates {providerName} after looking up {idpUserId}
    // (HandleAsync: admin check -> user lookup -> ProviderName.TryCreate -> provider lookup -> promote), so
    // a placeholder idpUserId would 404 before this case is reached — MalformedRouteParamData has no fixture
    // instance to supply a real seeded idpUserId, so this endpoint's case lives here instead, using the
    // seeded project owner's identity provider id as the trivial, well-formed {idpUserId}. Healing the
    // owner's seed state first (CreateProjectOwnerClientAsync) is required: the raw seed row needs the
    // same ensure-access repair every other owner-targeted test in this suite gets through that call.
    [Fact]
    public async Task Should_ReturnBadRequest_When_PromoteToProviderAdminProviderNameIsMalformed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Unused beyond its side effect: healing the owner's seed row before the Admin call below targets it.
        using var healedOwner = await fixture.CreateProjectOwnerClientAsync(ct);
        using var client = fixture.CreateAdminGatewayClient();
        var path = $"/api/v2/Users/{fixture.ProjectOwnerIdentityProviderId}/ProviderAdmin/{Whitespace}";
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative));

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Got {(int)response.StatusCode}.{Environment.NewLine}{raw}");
        using var body = JsonDocument.Parse(raw);
        // ProviderErrorCodes.InvalidName (Core.Domain) is unreachable here: IntegrationTests.csproj
        // references API/Gateway with ReferenceOutputAssembly="false" (black-box E2E, no assembly ref) —
        // literal mirrors the handler-path test's own literal at :93, for the same reason.
        Assert.Equal("provider.invalid_name", body.RootElement.GetProperty("code").GetString());
    }

    // Security precedence: an unauthenticated caller must be rejected at the auth gate before the route
    // param is ever inspected, so a malformed value never distinguishes "endpoint exists" from "endpoint
    // doesn't exist" for a caller who was never let in. Standalone method, not a Cases row — every row
    // above runs on the Admin client, which would always observe 400, never 401.
    [Fact]
    public async Task Should_ReturnUnauthorized_When_CallerIsUnauthenticatedAndRouteParamIsMalformed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateAnonymousGatewayClient();

        using var response = await anonymous.GetAsync(new Uri($"/api/v2/Projects/{Whitespace}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
