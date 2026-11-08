using Xunit;

namespace UserOrg.Tools.DatabaseMigrations.Tests;

// Placeholder substitution duplicates GatewayAttachFixture.SeedDatabaseAsync
// (Tests/E2E/E2E.Fixtures/GatewayAttachFixture.cs:275-280): a chain of ordinal string replacements.
// The tool cannot reference the test fixture, so the behaviour is pinned here instead.
public sealed class SeedScriptSubstituteTests
{
    private static Dictionary<string, string> Values(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void Should_Replace_Every_Occurrence_Of_A_Placeholder()
    {
        const string script = "INSERT INTO L VALUES ('__HEAPPE_MOCK_URL__'); UPDATE L SET E = '__HEAPPE_MOCK_URL__'";

        string result = SeedScript.Substitute(script, Values(("__HEAPPE_MOCK_URL__", "http://mock:1")));

        Assert.Equal("INSERT INTO L VALUES ('http://mock:1'); UPDATE L SET E = 'http://mock:1'", result);
    }

    [Fact]
    public void Should_Replace_Every_Placeholder_In_The_Dictionary()
    {
        const string script = "__A__ and __B__";

        string result = SeedScript.Substitute(script, Values(("__A__", "1"), ("__B__", "2")));

        Assert.Equal("1 and 2", result);
    }

    [Fact]
    public void Should_Match_Placeholder_Names_Ordinally()
    {
        // Ordinal, case-sensitive: '__x__' is not '__X__'.
        const string script = "__x__ __X__";

        string result = SeedScript.Substitute(script, Values(("__X__", "v")));

        Assert.Equal("__x__ v", result);
    }

    [Fact]
    public void Should_Leave_Unknown_Placeholders_Untouched()
    {
        const string script = "__KNOWN__ and __UNKNOWN__";

        string result = SeedScript.Substitute(script, Values(("__KNOWN__", "1")));

        Assert.Equal("1 and __UNKNOWN__", result);
    }

    [Fact]
    public void Should_Return_The_Script_Unchanged_When_There_Are_No_Values()
    {
        const string script = "SELECT '__STILL_HERE__'";

        string result = SeedScript.Substitute(script, Values());

        Assert.Equal(script, result);
    }

    [Fact]
    public void Should_Insert_A_Value_Containing_Dollar_And_Backslash_Literally()
    {
        // A Regex.Replace implementation would read '$0'/'$$' as substitution syntax and '\1' as an
        // escape — string.Replace must be used, so the value lands byte for byte.
        const string value = @"http://mock/$0\1$$";
        const string script = "UPDATE [dbo].[Location] SET [Endpoint] = '__HEAPPE_MOCK_URL__'";

        string result = SeedScript.Substitute(script, Values(("__HEAPPE_MOCK_URL__", value)));

        Assert.Equal($"UPDATE [dbo].[Location] SET [Endpoint] = '{value}'", result);
    }

    [Fact]
    public void Should_Accept_An_Empty_Value()
    {
        const string script = "prefix__EMPTY__suffix";

        string result = SeedScript.Substitute(script, Values(("__EMPTY__", "")));

        Assert.Equal("prefixsuffix", result);
    }
}