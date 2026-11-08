using Core.DataAccess.ResourceRequests;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Direct unit tests for the insert constraint-violation mapping (HIGH-2): a SQL uniqueness error is classified
/// by the offending constraint/index name so the endpoint maps it deterministically instead of a 500. The
/// in-memory provider cannot raise these SQL errors, so the mapping helper is tested directly.
/// </summary>
public sealed class ResourceRequestConstraintClassifierTests
{
    private static DbUpdateException With(string innerMessage) =>
        new("update failed", new InvalidOperationException(innerMessage));

    [Fact]
    public void Should_ClassifySingleActive_When_FilteredIndexViolated()
    {
        var ex = With($"Cannot insert duplicate key row ... '{ResourceRequestConstraintClassifier.SingleActiveIndexName}'.");

        Assert.Equal(ResourceRequestConflict.SingleActive, ResourceRequestConstraintClassifier.Classify(ex));
    }

    [Fact]
    public void Should_ClassifySlug_When_PrimaryKeyViolated()
    {
        var ex = With($"Violation of PRIMARY KEY constraint '{ResourceRequestConstraintClassifier.SlugPrimaryKeyName}'.");

        Assert.Equal(ResourceRequestConflict.Slug, ResourceRequestConstraintClassifier.Classify(ex));
    }

    [Fact]
    public void Should_ReturnNull_When_UnrelatedUpdateException()
    {
        var ex = With("Some unrelated FK violation on another table.");

        Assert.Null(ResourceRequestConstraintClassifier.Classify(ex));
    }
}
