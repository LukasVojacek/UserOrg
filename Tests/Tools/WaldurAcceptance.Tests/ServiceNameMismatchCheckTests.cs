using UserOrg.Tools.WaldurAcceptance.Core;

using Xunit;

namespace UserOrg.Tools.WaldurAcceptance.Tests;

public sealed class ServiceNameMismatchCheckTests
{
    [Fact]
    public void Should_Build_Per_Table_Per_ServiceName_Histogram()
    {
        ServiceNameRow[] rows =
        [
            new("Project", "EFP-Waldur", "p1"),
            new("Project", "EFP-Waldur", "p2"),
            new("Project", "Puhuri-Waldur", "p3"),
            new("Provider", "EFP-Waldur", "prov1"),
        ];

        var histogram = ServiceNameMismatchCheck.Histogram(rows);

        Assert.Equal(2, histogram["Project"]["EFP-Waldur"]);
        Assert.Equal(1, histogram["Project"]["Puhuri-Waldur"]);
        Assert.Equal(1, histogram["Provider"]["EFP-Waldur"]);
    }

    [Fact]
    public void Should_Count_Only_Owned_Rows_Carrying_Foreign_ServiceName()
    {
        // p1/p2 are known Puhuri projects; one of them was written with the wrong (EFP) ServiceName.
        // p3 is an EFP project (not owned) and must not be counted despite its EFP ServiceName.
        ServiceNameRow[] rows =
        [
            new("Project", "EFP-Waldur", "p1"),      // owned + wrong -> counts
            new("Project", "Puhuri-Waldur", "p2"),   // owned + correct -> no
            new("Project", "EFP-Waldur", "p3"),      // not owned -> no
            new("Provider", "EFP-Waldur", "prov1"),  // owned + wrong -> counts
        ];
        var owned = new HashSet<string>(StringComparer.Ordinal) { "p1", "p2", "prov1" };

        var result = ServiceNameMismatchCheck.CountOwnedMismatches(rows, owned, "Puhuri-Waldur");

        Assert.Equal(1, result["Project"]);
        Assert.Equal(1, result["Provider"]);
    }

    [Fact]
    public void Should_Return_Empty_When_All_Owned_Rows_Match()
    {
        ServiceNameRow[] rows =
        [
            new("Project", "Puhuri-Waldur", "p1"),
            new("Provider", "Puhuri-Waldur", "prov1"),
        ];
        var owned = new HashSet<string>(StringComparer.Ordinal) { "p1", "prov1" };

        var result = ServiceNameMismatchCheck.CountOwnedMismatches(rows, owned, "Puhuri-Waldur");

        Assert.Empty(result);
    }
}
