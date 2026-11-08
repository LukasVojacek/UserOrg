using UserOrganizationManagement.API.API;

public sealed class ProjectEndpointHeaderTests
{
    [Fact]
    public void ToAsciiHeaderValue_WhenValueContainsEnDash_ReplacesItWithPlaceholder()
    {
        var value = ProjectEndpoint.ToAsciiHeaderValue("IT4Innovations – Karolina / R1 (Loc)");

        Assert.Equal("IT4Innovations ? Karolina / R1 (Loc)", value);
        Assert.All(value, c => Assert.InRange(c, ' ', '~'));
    }

    [Fact]
    public void ToAsciiHeaderValue_WhenValueIsAscii_ReturnsItUnchanged()
    {
        const string value = "Provider / R1 (Loc-A, Loc-B); Provider / R2 (Loc-C)";

        Assert.Equal(value, ProjectEndpoint.ToAsciiHeaderValue(value));
    }
}
