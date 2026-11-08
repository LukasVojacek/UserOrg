using Xunit;

namespace UserOrg.Tools.DatabaseMigrations.Tests;

// The SEED mode of the DatabaseMigrations tool parses its own command line:
//     DatabaseMigrations seed --script <path> [--set NAME=VALUE]...
// args[0] is the mode token and is skipped, exactly as GenerateScriptAsync (MigrationRunner.cs:177)
// and ApplyMigrationsAsync (:525) already do — both loop from index 1.
public sealed class SeedScriptParseTests
{
    [Fact]
    public void Should_Return_Script_Path_And_Every_Set_Value()
    {
        var options = SeedScript.Parse(["seed", "--script", "x.sql", "--set", "A=1", "--set", "B=two=2"]);

        Assert.Equal("x.sql", options.ScriptPath);
        Assert.Equal(2, options.Values.Count);
        Assert.Equal("1", options.Values["A"]);
        // Only the FIRST '=' separates the name from the value; the value may itself contain '='.
        Assert.Equal("two=2", options.Values["B"]);
    }

    [Fact]
    public void Should_Return_Empty_Values_When_No_Set_Is_Given()
    {
        var options = SeedScript.Parse(["seed", "--script", "seed-data.sql"]);

        Assert.Equal("seed-data.sql", options.ScriptPath);
        Assert.Empty(options.Values);
    }

    [Fact]
    public void Should_Keep_The_Placeholder_Names_The_AppHost_Passes()
    {
        // The shape db-seed-e2e will actually invoke (T-059-03 piece 2).
        var options = SeedScript.Parse(
        [
            "seed",
            "--script", "/repo/Tests/E2E/E2E.Fixtures/seed-data.sql",
            "--set", "__HEAPPE_MOCK_URL__=http://localhost:5123",
            "--set", "__PROJECT_OWNER_IDP_ID__=test",
        ]);

        Assert.Equal("/repo/Tests/E2E/E2E.Fixtures/seed-data.sql", options.ScriptPath);
        Assert.Equal("http://localhost:5123", options.Values["__HEAPPE_MOCK_URL__"]);
        Assert.Equal("test", options.Values["__PROJECT_OWNER_IDP_ID__"]);
    }

    [Fact]
    public void Should_Throw_Naming_Script_When_The_Script_Option_Is_Missing()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SeedScript.Parse(["seed", "--set", "A=1"]));

        Assert.Contains("--script", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Throw_Naming_Script_When_The_Script_Value_Is_Missing()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SeedScript.Parse(["seed", "--script"]));

        Assert.Contains("--script", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Throw_Naming_The_Offending_Token_When_A_Set_Has_No_Equals()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SeedScript.Parse(["seed", "--script", "x.sql", "--set", "NOEQUALSHERE"]));

        Assert.Contains("NOEQUALSHERE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Throw_Naming_Set_When_The_Set_Value_Is_Missing()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SeedScript.Parse(["seed", "--script", "x.sql", "--set"]));

        Assert.Contains("--set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Throw_Naming_The_Option_When_It_Is_Unknown()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SeedScript.Parse(["seed", "--script", "x.sql", "--bogus", "1"]));

        Assert.Contains("--bogus", exception.Message, StringComparison.Ordinal);
    }
}