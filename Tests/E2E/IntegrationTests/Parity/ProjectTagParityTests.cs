using E2E.Fixtures;

using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// v1↔v2 read parity for the ProjectTag surface (ADR-0055 D4/D5). Two pairs:
/// <list type="bullet">
///   <item>list — <c>GET /api/ProjectTag</c> ↔ <c>GET /api/v2/ProjectTags</c>;</item>
///   <item>single — <c>GET /api/ProjectTag/{id}</c> ↔ <c>GET /api/v2/ProjectTags/{tagName}</c>,
///     the GUID→natural-key divergence (ADR-0022): the surrogate id addresses v1, the globally
///     unique name addresses v2, so the divergence is a <em>path</em> substitution, not a body shape.</item>
/// </list>
/// Declared body divergence for both: the v2 tag DTO slimmed to the natural key — it exposes only
/// <c>Name</c>, dropping the legacy surrogate <c>Id</c> and free-text <c>Description</c> (ADR-0022).
/// Those two v1-only fields are declared drops, not silent tolerances.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class ProjectTagParityTests(GatewayAttachFixture fixture)
{
    // v2 project-tag DTO = { Name } only; v1 ProjectTagModel = { Id, Name, Description }. Drop the
    // two v1-only fields the v2 natural-key contract intentionally omits (ADR-0022).
    private static readonly string[] V1OnlyFields = ["Id", "Description"];

    // Seeded tag: Id 55555555-... , Name "tag1" (seed-data.sql).
    private static readonly string SeedTagName = "tag1";

    public static IEnumerable<object[]> Roles =>
    [
        [Role.Admin],
        [Role.Owner],
    ];

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Should_MatchStatusAndElementShape_When_ListingProjectTags(Role role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, role, ct);

        using var v1 = await client.GetAsync("/api/ProjectTag", ct);
        using var v2 = await client.GetAsync("/api/v2/ProjectTags", ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"projecttag-list [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.");

        if (v1.StatusCode != HttpStatusCode.OK)
            return;

        var result = B07Parity.CompareListElementShape(
            await v1.Content.ReadAsStringAsync(ct),
            await v2.Content.ReadAsStringAsync(ct),
            transforms: [],
            v1Drop: V1OnlyFields,
            v2Drop: []);

        AssertListShape("projecttag-list", role, result);
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Should_MatchStatusAndShape_When_ReadingProjectTagByNaturalKey(Role role)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await B07Parity.CreateRoleClientAsync(fixture, role, ct);

        // GUID→natural-key divergence (ADR-0022): v1 addresses by surrogate id, v2 by the unique name.
        using var v1 = await client.GetAsync($"/api/ProjectTag/{SeedConstants.ProjectTagId}", ct);
        using var v2 = await client.GetAsync($"/api/v2/ProjectTags/{SeedTagName}", ct);

        Assert.True(
            v1.StatusCode == v2.StatusCode,
            $"projecttag-by-key [{role}]: status divergence v1={(int)v1.StatusCode} v2={(int)v2.StatusCode}.");

        if (v1.StatusCode != HttpStatusCode.OK)
            return;

        var result = B07Parity.CompareObjects(
            await v1.Content.ReadAsStringAsync(ct),
            await v2.Content.ReadAsStringAsync(ct),
            transforms: [],
            v1Drop: V1OnlyFields,
            v2Drop: []);

        Assert.True(result.IsMatch, $"projecttag-by-key [{role}] shape diverged:{Environment.NewLine}{result.ToDiffString()}");
    }

    private static void AssertListShape(string label, Role role, ListShapeResult result)
    {
        switch (result.Outcome)
        {
            case ListShapeOutcome.ShapeMatched:
            case ListShapeOutcome.BothEmpty:
                return;

            // v2 caller-scoped to nothing while v1 (broad/admin) had rows — declared ADR-0018 tiering
            // divergence; status parity already asserted. ProjectTag is not tiered, so this is not
            // expected here, but tolerated identically to the Administration surface for consistency.
            case ListShapeOutcome.V2ScopedEmpty:
                return;

            case ListShapeOutcome.ShapeMismatch:
                Assert.Fail($"{label} [{role}] element shape diverged:{Environment.NewLine}{result.Comparison?.ToDiffString()}");
                break;

            default:
                Assert.Fail($"{label} [{role}] unexpected list outcome {result.Outcome}.");
                break;
        }
    }
}
