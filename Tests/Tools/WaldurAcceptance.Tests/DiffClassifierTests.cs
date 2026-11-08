using UserOrg.Tools.WaldurAcceptance.Core;

using Xunit;

namespace UserOrg.Tools.WaldurAcceptance.Tests;

public sealed class DiffClassifierTests
{
    private static NaturalKeyRow Row(string entityType, string key, params (string Field, string? Value)[] fields) =>
        new(entityType, key, fields.ToDictionary(f => f.Field, f => f.Value, StringComparer.Ordinal));

    private static DiffReport Classify(IReadOnlyList<NaturalKeyRow> old, IReadOnlyList<NaturalKeyRow> @new) =>
        DiffClassifier.Classify(old, @new, ProjectionDefaults.ExcludedFields, ExpectedDifferences.All);

    [Fact]
    public void Should_Report_Clean_When_Rows_Match()
    {
        var old = new[] { Row(EntityTypes.Project, "p1|EFP-Waldur", ("Name", "Alpha")) };
        var @new = new[] { Row(EntityTypes.Project, "p1|EFP-Waldur", ("Name", "Alpha")) };

        var report = Classify(old, @new);

        Assert.Equal(AcceptanceOutcome.Clean, report.Outcome);
        Assert.Equal(1, report.EqualRowCount);
        Assert.Empty(report.Deltas);
    }

    [Fact]
    public void Should_Ignore_Excluded_Field_When_Only_It_Differs()
    {
        var old = new[] { Row(EntityTypes.Project, "p1|EFP-Waldur", ("Name", "Alpha"), ("ModifiedAt", "2026-01-01")) };
        var @new = new[] { Row(EntityTypes.Project, "p1|EFP-Waldur", ("Name", "Alpha"), ("ModifiedAt", "2026-07-07")) };

        var report = Classify(old, @new);

        Assert.Equal(AcceptanceOutcome.Clean, report.Outcome);
        Assert.Equal(1, report.EqualRowCount);
        Assert.Empty(report.Deltas);
    }

    [Fact]
    public void Should_Report_NeedsConfirmation_For_Removed_Project_ExpectedDifference()
    {
        var old = Array.Empty<NaturalKeyRow>();
        var @new = new[] { Row(EntityTypes.Project, "gone|EFP-Waldur", ("Name", "Gone"), ("IsDeleted", "1")) };

        var report = Classify(old, @new);

        // Expected differences are NOT a clean pass — they need human sign-off.
        Assert.Equal(AcceptanceOutcome.NeedsConfirmation, report.Outcome);
        var delta = Assert.Single(report.Expected);
        Assert.Contains("removed-project-now-lands-as-deleted", delta.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Report_NeedsConfirmation_For_New_Only_Membership()
    {
        var old = Array.Empty<NaturalKeyRow>();
        var @new = new[] { Row(EntityTypes.AssignedUserToProject, "p1|jdoe") };

        var report = Classify(old, @new);

        Assert.Equal(AcceptanceOutcome.NeedsConfirmation, report.Outcome);
        var delta = Assert.Single(report.Expected);
        Assert.Contains("per-user-vs-all-or-nothing-membership", delta.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Fail_When_Business_Field_Differs()
    {
        var old = new[] { Row(EntityTypes.Project, "p1|EFP-Waldur", ("Name", "Alpha")) };
        var @new = new[] { Row(EntityTypes.Project, "p1|EFP-Waldur", ("Name", "Beta")) };

        var report = Classify(old, @new);

        Assert.Equal(AcceptanceOutcome.Fail, report.Outcome);
        var delta = Assert.Single(report.Unexplained);
        Assert.Equal(DeltaKind.FieldMismatch, delta.Delta.Kind);
        Assert.Equal("Name", delta.Delta.Field);
    }

    [Fact]
    public void Should_Fail_When_Provider_Only_In_Old()
    {
        var old = new[] { Row(EntityTypes.Provider, "prov1|EFP-Waldur", ("Name", "SiteA")) };
        var @new = Array.Empty<NaturalKeyRow>();

        var report = Classify(old, @new);

        Assert.Equal(AcceptanceOutcome.Fail, report.Outcome);
        var delta = Assert.Single(report.Unexplained);
        Assert.Equal(DeltaKind.OnlyInOld, delta.Delta.Kind);
    }

    [Fact]
    public void Should_Emit_Unexplained_DuplicateIdentity_When_Key_Repeats_On_A_Side()
    {
        // Same natural key projecting to two rows (e.g. ProjectResource Name+ServiceName across
        // providers, or soft-deleted + live). Must report, not throw.
        var old = new[]
        {
            Row(EntityTypes.ProjectResource, "res1|EFP-Waldur", ("Description", "A")),
            Row(EntityTypes.ProjectResource, "res1|EFP-Waldur", ("Description", "B")),
        };
        var @new = new[] { Row(EntityTypes.ProjectResource, "res1|EFP-Waldur", ("Description", "A")) };

        var report = Classify(old, @new);

        Assert.Equal(AcceptanceOutcome.Fail, report.Outcome);
        var delta = Assert.Single(report.Unexplained);
        Assert.Equal(DeltaKind.DuplicateIdentity, delta.Delta.Kind);
    }
}
