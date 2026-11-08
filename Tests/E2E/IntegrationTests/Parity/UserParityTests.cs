using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// v1↔v2 read parity for the current-user surface (ADR-0055 D4/D5), all under an authenticated caller:
/// <list type="bullet">
///   <item><c>GET /api/User/Preferences</c> ↔ <c>GET /api/v2/Users/Me/Preferences</c> — the opaque
///     per-user preferences blob; parity is status + the same stored value. v1 returns the blob as a
///     bare JSON-encoded string, v2 wraps the parsed JSON in a <c>{"Preferences": {…}}</c> envelope,
///     so the value is unwrapped on each side before comparing.</item>
///   <item><c>GET /api/UserInfo</c> ↔ <c>GET /api/v2/UserInfo</c>.</item>
///   <item><c>GET /api/UserInfo/Extended</c> ↔ <c>GET /api/v2/UserInfo/Extended</c>.</item>
/// </list>
/// Declared UserInfo divergences: the v2 identity contract exposes <c>IdentityProviderUserId</c> where
/// v1 exposed the internal surrogate <c>Id</c> plus the Keycloak session id <c>KeycloakSid</c> (declared
/// drops on each side), and renders each role's <c>ProjectShortName</c> bare where v1 envelopes it.
/// Extended additionally: the v2 role record carries <c>IdpUserId</c> the legacy extended element did not,
/// and its per-role <c>ProjectResources</c> use the slim v2 resource projection — whose own v1↔v2 shape
/// parity is B06's concern (ResourceProviderLocationByIdParityTests), so it is dropped here and the
/// extended-role <em>envelope</em> shape is what this asserts.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class UserParityTests(GatewayAttachFixture fixture)
{
    public static IEnumerable<object[]> Roles =>
    [
        [Role.Admin],
        [Role.Owner],
    ];

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Should_MatchStatusAndValue_When_ReadingUserPreferences(Role role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, role, ct);

        using var v1 = await client.GetAsync("/api/User/Preferences", ct);
        using var v2 = await client.GetAsync("/api/v2/Users/Me/Preferences", ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"user-preferences [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.");

        if (v1.StatusCode != HttpStatusCode.OK)
            return;

        var v1Body = await v1.Content.ReadAsStringAsync(ct);
        var v2Body = await v2.Content.ReadAsStringAsync(ct);

        // v1 re-encodes the stored preferences JSON as a bare quoted string; unwrap it to the JSON it
        // represents. v1 coalesces a missing row to "{}", so this is never null in practice. A corrupt
        // stored row is itself a divergence — v2 swallows it and returns "{}" — so an unparseable body
        // has to fail as a parity assertion, not as a bare JsonException.
        var v1Preferences = ParseOrFail(
            () => JsonNode.Parse(JsonSerializer.Deserialize<string>(v1Body) ?? "null"), "v1 preferences", role, v1Body);

        // The "Preferences" lookup is deliberately case-sensitive: HostInfrastructureExtensions sets
        // PropertyNamingPolicy = null, so a silent slide back to camelCase must fail here. Nobody can run
        // this suite without Docker, so every failure below carries the raw v2 body as the diagnostic.
        using var v2Document = ParseOrFail(() => JsonDocument.Parse(v2Body), "v2 body", role, v2Body);
        var v2Root = v2Document.RootElement;

        Assert.True(
            v2Root.ValueKind == JsonValueKind.Object,
            $$"""user-preferences [{{role}}]: v2 body is {{v2Root.ValueKind}}, expected a {"Preferences": …} envelope object. Body: {{v2Body}}""");

        Assert.True(
            v2Root.TryGetProperty("Preferences", out var v2Envelope),
            $"""user-preferences [{role}]: v2 envelope has no "Preferences" key — casing regression? Body: {v2Body}""");

        var v2Preferences = JsonNode.Parse(v2Envelope.GetRawText());

        // Same caller, same stored row: the unwrapped blobs must be semantically equal — same keys and the
        // same values. Both sides are parsed first, so key order and whitespace do not matter.
        Assert.True(
            JsonNode.DeepEquals(v1Preferences, v2Preferences),
            $"user-preferences [{role}] value divergence:{Environment.NewLine}v1={v1Preferences?.ToJsonString() ?? "null"}{Environment.NewLine}v2={v2Preferences?.ToJsonString() ?? "null"}");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Should_MatchStatusAndShape_When_ReadingUserInfo(Role role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, role, ct);

        using var v1 = await client.GetAsync("/api/UserInfo", ct);
        using var v2 = await client.GetAsync("/api/v2/UserInfo", ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"userinfo [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.");

        Assert.Equal(HttpStatusCode.OK, v2.StatusCode);

        var transforms = SpecToComparer.ToTransforms([new ValueObjectEnvelope("ProjectShortName")]);

        // Contract-shape (name + kind) parity; role count / null profile fields / role ordering differ by
        // content — v2 scopes roles per the caller (ADR-0018) and sources them from the Forge role service.
        var diffs = B07Parity.CompareContractShape(
            await v1.Content.ReadAsStringAsync(ct),
            await v2.Content.ReadAsStringAsync(ct),
            transforms,
            v1Drop: ["Id", "KeycloakSid"],
            v2Drop: ["IdentityProviderUserId"]);

        Assert.True(diffs.Count == 0, $"userinfo [{role}] contract shape diverged:{Environment.NewLine}{string.Join(Environment.NewLine, diffs)}");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Should_MatchStatusAndShape_When_ReadingExtendedUserInfo(Role role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, role, ct);

        using var v1 = await client.GetAsync("/api/UserInfo/Extended", ct);
        using var v2 = await client.GetAsync("/api/v2/UserInfo/Extended", ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"userinfo-extended [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.");

        Assert.Equal(HttpStatusCode.OK, v2.StatusCode);

        var transforms = SpecToComparer.ToTransforms([new ValueObjectEnvelope("ProjectShortName")]);

        // Contract-shape parity of the extended-role envelope; role count / null fields / ordering differ by
        // content (ADR-0018 scoping). Per-role ProjectResources use the slim v2 resource projection whose
        // v1↔v2 shape parity is B06's concern, so it is dropped here.
        // IsDeleted is dropped because v1 has no deactivation gate and has to hand the caller the flag,
        // while v2 gates in CallerProvisioning and answers 403 user.deactivated — so a v2 200 body could
        // only ever carry false. The field's v2 home is the DDI sync read, where a consumer can act on it
        // (ADR-0103).
        var diffs = B07Parity.CompareContractShape(
            await v1.Content.ReadAsStringAsync(ct),
            await v2.Content.ReadAsStringAsync(ct),
            transforms,
            v1Drop: ["Id", "KeycloakSid", "ProjectResources", "IsDeleted"],
            v2Drop: ["IdentityProviderUserId", "IdpUserId", "ProjectResources"]);

        Assert.True(diffs.Count == 0, $"userinfo-extended [{role}] contract shape diverged:{Environment.NewLine}{string.Join(Environment.NewLine, diffs)}");
    }

    /// <summary>
    /// Runs a JSON parse, turning an unparseable body into a parity failure that names the side, the
    /// role and the raw body — this suite only runs on a Docker-capable runner, so the message is the
    /// only diagnostic whoever triggers it will get.
    /// </summary>
    private static T ParseOrFail<T>(Func<T> parse, string side, Role role, string rawBody)
    {
        try
        {
            return parse();
        }
        catch (JsonException e)
        {
            Assert.Fail($"user-preferences [{role}]: {side} is not valid JSON — {e.Message} Body: {rawBody}");
            return default!;
        }
    }
}
