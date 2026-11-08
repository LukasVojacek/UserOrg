namespace ParityTests.Spec;

/// <summary>
/// Thin unit tests for the declarative parity spec model (ADR-0055 D4). Records are
/// mostly compiler-verified; these assert construction, value-equality, and that the
/// discriminated unions pattern-match the way B05/B06 will consume them. No Aspire boot.
/// </summary>
public sealed class ParitySpecModelTests
{
    [Fact]
    public void Should_ExposeAllFields_When_ParitySpecConstructed()
    {
        var spec = new ParitySpec(
            Name: "providers list",
            V1Path: "/api/Provider",
            V2Path: "/api/v2/Providers",
            Roles: [Role.Admin, Role.Owner],
            Shape: new MustMatchFields("name"),
            Known: [new ValueObjectEnvelope("name")]);

        Assert.Equal("providers list", spec.Name);
        Assert.Equal("/api/Provider", spec.V1Path);
        Assert.Equal("/api/v2/Providers", spec.V2Path);
        Assert.Equal([Role.Admin, Role.Owner], spec.Roles);
        Assert.Single(spec.Known);
    }

    [Fact]
    public void Should_MapOneToOneToFixtureFactories_When_RoleEnumEnumerated()
    {
        // 1:1 with GatewayAttachFixture dev-auth client factories (B05 wires these).
        Assert.Equal(
            [Role.Admin, Role.Owner, Role.Manager, Role.Member, Role.Anonymous],
            Enum.GetValues<Role>());
    }

    [Fact]
    public void Should_PatternMatchStructural_When_ShapeRuleIsStructural()
    {
        ShapeRule shape = new Structural();

        var matched = shape switch
        {
            MustMatchFields => false,
            Structural => true,
            _ => false,
        };

        Assert.True(matched);
    }

    [Fact]
    public void Should_CarryPaths_When_ShapeRuleIsMustMatchFields()
    {
        ShapeRule shape = new MustMatchFields("id", "name");

        var paths = shape switch
        {
            MustMatchFields m => m.Paths,
            _ => [],
        };

        Assert.Equal(["id", "name"], paths);
    }

    [Fact]
    public void Should_PatternMatchEachCase_When_DivergenceMatched()
    {
        Divergence[] cases =
        [
            new FieldRenamed("shortName", "name"),
            new ValueObjectEnvelope("shortName"),
            new StatusRemap(HttpStatusCode.OK, HttpStatusCode.NotFound),
        ];

        var labels = cases.Select(d => d switch
        {
            FieldRenamed => "rename",
            ValueObjectEnvelope => "envelope",
            StatusRemap => "status",
            _ => "unknown",
        });

        Assert.Equal(["rename", "envelope", "status"], labels);
    }

    [Fact]
    public void Should_ExposeCaseData_When_DivergenceConstructed()
    {
        var renamed = new FieldRenamed("shortName", "name");
        var envelope = new ValueObjectEnvelope("providerName");
        var remap = new StatusRemap(HttpStatusCode.OK, HttpStatusCode.NotFound);

        Assert.Equal(("shortName", "name"), (renamed.From, renamed.To));
        Assert.Equal("providerName", envelope.Path);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (remap.V1, remap.V2));
    }

    [Fact]
    public void Should_BeValueEqual_When_SameData()
    {
        Assert.Equal(new FieldRenamed("a", "b"), new FieldRenamed("a", "b"));
        Assert.Equal(new ValueObjectEnvelope("p"), new ValueObjectEnvelope("p"));
        Assert.Equal(new Structural(), new Structural());
        Assert.NotEqual<Divergence>(new FieldRenamed("a", "b"), new ValueObjectEnvelope("a"));
    }
}
