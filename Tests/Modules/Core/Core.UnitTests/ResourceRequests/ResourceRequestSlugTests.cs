using Core.DataAccess.ResourceRequests;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Pure tests for the <c>{projectShortName}-{resourceName}</c> lower-kebab slug with ordinal-suffix
/// collision handling (ADR-0066). The generator is pure — the count of prior (terminal) requests for the key
/// is resolved by the caller and passed in.
/// </summary>
public sealed class ResourceRequestSlugTests
{
    [Theory]
    [InlineData("MyProject", "GPU Cluster", "myproject-gpu-cluster")]
    [InlineData("proj_01", "res.A/B", "proj-01-res-a-b")]
    [InlineData("  Spaced  ", "--Edge--", "spaced-edge")]
    public void Should_NormalizeToLowerKebab_When_SlugBuilt(string project, string resource, string expected)
    {
        Assert.Equal(expected, ResourceRequestSlug.Create(project, resource, existingBaseSlugCount: 0));
    }

    [Theory]
    [InlineData(0, "myproject-gpu")]
    [InlineData(1, "myproject-gpu-2")]
    [InlineData(2, "myproject-gpu-3")]
    public void Should_AppendOrdinalSuffix_When_BaseSlugAlreadyExists(int existingCount, string expected)
    {
        Assert.Equal(expected, ResourceRequestSlug.Create("MyProject", "GPU", existingCount));
    }

    [Fact]
    public void Should_SuffixSlug_When_SameResourceNameOfferedByAnotherProvider()
    {
        // Project already holds an (active) request for "gpu" from provider A; a new request for a same-named
        // "gpu" resource from provider B is legitimate under the 3-part invariant but shares the base slug, so
        // it must be suffixed rather than collide on the PK.
        var providerA = ResourceRequestSlug.Create("proj", "gpu", existingBaseSlugCount: 0);
        var providerB = ResourceRequestSlug.Create("proj", "gpu", existingBaseSlugCount: 1);

        Assert.Equal("proj-gpu", providerA);
        Assert.Equal("proj-gpu-2", providerB);
        Assert.NotEqual(providerA, providerB);
    }

    [Fact]
    public void Should_Throw_When_ComponentsProduceEmptySlug()
    {
        Assert.Throws<ArgumentException>(() => ResourceRequestSlug.Create("!!!", "@@@", 0));
    }

    [Fact]
    public void Should_ReturnBareSlug_When_BaseSlugFree()
    {
        Assert.Equal("proj-gpu", ResourceRequestSlug.NextAvailable("proj", "gpu", []));
    }

    [Fact]
    public void Should_NotSuffix_When_OnlyDistinctResourcesOrdinalFormExists()
    {
        // A distinct resource "gpu-2" owns the slug "proj-gpu-2"; it must NOT push "gpu"'s ordinal — the base
        // "proj-gpu" is still free, so a new "gpu" request takes the bare slug (HIGH-1 anti-greedy).
        Assert.Equal("proj-gpu", ResourceRequestSlug.NextAvailable("proj", "gpu", ["proj-gpu-2"]));
    }

    [Fact]
    public void Should_NotSuffix_When_OnlySharedBaseOfDistinctResourceExists()
    {
        // Vice versa: an existing "proj-gpu" must not force a suffix onto the distinct resource "gpu-2".
        Assert.Equal("proj-gpu-2", ResourceRequestSlug.NextAvailable("proj", "gpu-2", ["proj-gpu"]));
    }

    [Fact]
    public void Should_TakeLowestFreeOrdinal_When_BaseAndSomeOrdinalsTaken()
    {
        Assert.Equal("proj-gpu-2", ResourceRequestSlug.NextAvailable("proj", "gpu", ["proj-gpu"]));
        Assert.Equal("proj-gpu-3", ResourceRequestSlug.NextAvailable("proj", "gpu", ["proj-gpu", "proj-gpu-2"]));
        // A gap (missing -2) is filled rather than skipped, so the slug can never collide with an existing PK.
        Assert.Equal("proj-gpu-2", ResourceRequestSlug.NextAvailable("proj", "gpu", ["proj-gpu", "proj-gpu-3"]));
    }
}
