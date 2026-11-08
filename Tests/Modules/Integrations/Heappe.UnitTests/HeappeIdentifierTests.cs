using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

/// <summary>
/// The HEAppE identifier names an instance in the Nexus proxy path (<c>/api/heappe/{identifier}/</c>), so it is
/// normalised (trimmed, lower case) and restricted to the proxy-safe shape <c>^[a-z0-9]([a-z0-9_-]*[a-z0-9])?$</c>,
/// 64 at most: <c>_</c> and <c>-</c> only inside, never first or last.
/// </summary>
public sealed class HeappeIdentifierTests
{
    [Fact]
    public void TryParse_trims_and_lower_cases_the_identifier()
    {
        var parsed = HeappeIdentifier.TryParse(" HEAppE-Mock ", out var identifier);

        Assert.True(parsed);
        Assert.Equal("heappe-mock", identifier.Value);
    }

    [Fact]
    public void ToString_returns_the_normalised_identifier()
    {
        Assert.True(HeappeIdentifier.TryParse(" HEAppE-Mock ", out var identifier));

        Assert.Equal("heappe-mock", identifier.ToString());
    }

    [Fact]
    public void TryParse_treats_identifiers_differing_only_in_case_as_equal()
    {
        Assert.True(HeappeIdentifier.TryParse("HEAPPE-MOCK", out var upper));
        Assert.True(HeappeIdentifier.TryParse("heappe-mock", out var lower));

        Assert.Equal(lower, upper);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-x")]
    [InlineData("x-")]
    [InlineData("_x")]
    [InlineData("x_")]
    [InlineData("a/b")]
    [InlineData("a b")]
    public void TryParse_rejects_a_value_outside_the_proxy_safe_shape(string? value)
    {
        Assert.False(HeappeIdentifier.TryParse(value, out _));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    [InlineData("a_-b")]
    public void TryParse_accepts_underscore_and_hyphen_inside_the_identifier(string value)
    {
        Assert.True(HeappeIdentifier.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_accepts_exactly_sixty_four_characters()
    {
        Assert.True(HeappeIdentifier.TryParse(new string('a', HeappeIdentifier.MaxLength), out _));
    }

    [Fact]
    public void TryParse_rejects_sixty_five_characters()
    {
        Assert.False(HeappeIdentifier.TryParse(new string('a', HeappeIdentifier.MaxLength + 1), out _));
    }
}
