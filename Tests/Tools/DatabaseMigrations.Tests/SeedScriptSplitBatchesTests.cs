using Xunit;

namespace UserOrg.Tools.DatabaseMigrations.Tests;

// GO is a client-side batch separator, not T-SQL: SqlCommand rejects a script that contains it.
// The split duplicates GatewayAttachFixture.SeedDatabaseAsync
// (Tests/E2E/E2E.Fixtures/GatewayAttachFixture.cs:281-283):
//     Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
//          .Where(batch => !string.IsNullOrWhiteSpace(batch))
public sealed class SeedScriptSplitBatchesTests
{
    [Fact]
    public void Should_Return_One_Batch_When_The_Script_Has_No_Go_Line()
    {
        const string script = "SELECT 1\nSELECT 2";

        var batches = SeedScript.SplitBatches(script);

        Assert.Equal("SELECT 1\nSELECT 2", Assert.Single(batches).Trim());
    }

    [Fact]
    public void Should_Split_On_A_Go_Line()
    {
        const string script = "SELECT 1\nGO\nSELECT 2\nGO\nSELECT 3";

        var batches = SeedScript.SplitBatches(script);

        Assert.Equal(3, batches.Count);
        Assert.Equal("SELECT 1", batches[0].Trim());
        Assert.Equal("SELECT 2", batches[1].Trim());
        Assert.Equal("SELECT 3", batches[2].Trim());
    }

    [Theory]
    [InlineData("GO")]
    [InlineData("go")]
    [InlineData("Go")]
    [InlineData("  GO  ")]
    [InlineData("\tGO\t")]
    public void Should_Split_Regardless_Of_Case_Or_Surrounding_Whitespace(string separatorLine)
    {
        string script = $"SELECT 1\n{separatorLine}\nSELECT 2";

        var batches = SeedScript.SplitBatches(script);

        Assert.Equal(2, batches.Count);
        Assert.Equal("SELECT 1", batches[0].Trim());
        Assert.Equal("SELECT 2", batches[1].Trim());
    }

    [Fact]
    public void Should_Not_Split_On_Go_Embedded_In_A_Line()
    {
        // GOTO / GONE / a quoted 'GO' are ordinary script text: only a line that is exactly GO separates.
        const string script = "SELECT 1\nGOTO done\nSELECT 'GO'\nGONE\nSELECT 2";

        var batches = SeedScript.SplitBatches(script);

        Assert.Equal(script, Assert.Single(batches).Trim());
    }

    [Fact]
    public void Should_Drop_Blank_Batches()
    {
        const string script = "GO\n\nGO\nSELECT 1\nGO\n   \nGO\n";

        var batches = SeedScript.SplitBatches(script);

        Assert.Equal("SELECT 1", Assert.Single(batches).Trim());
    }

    [Fact]
    public void Should_Return_No_Batches_When_The_Script_Is_Blank()
    {
        const string script = "\n   \nGO\n";

        var batches = SeedScript.SplitBatches(script);

        Assert.Empty(batches);
    }

    [Fact]
    public void Should_Keep_The_Batch_Order_Of_The_Script()
    {
        const string script = "INSERT A\nGO\nUPDATE B\nGO\nDELETE C";

        var batches = SeedScript.SplitBatches(script);

        string[] expected = ["INSERT A", "UPDATE B", "DELETE C"];
        Assert.Equal(expected, batches.Select(batch => batch.Trim()).ToArray());
    }
}