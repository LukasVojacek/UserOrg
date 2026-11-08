using Core.Domain.Provider;

namespace Core.UnitTests.Providers;

public sealed class AllocationSlugTests
{
    [Fact]
    public void Should_BuildSlug_FromResourceName()
    {
        var slug = AllocationSlug.New(ResourceName.Create("OPEN-33-82"));

        Assert.StartsWith("asg-open-33-82-", slug);
        var r6 = slug["asg-open-33-82-".Length..];
        Assert.Matches("^[a-z0-9]{6}$", r6);
    }

    [Fact]
    public void Should_DifferOnRepeat_ForSameResource()
    {
        var resource = ResourceName.Create("OPEN-33-82");

        // The r6 suffix exists precisely so two allocations of the same resource are distinctly addressable.
        Assert.NotEqual(AllocationSlug.New(resource), AllocationSlug.New(resource));
    }
}
