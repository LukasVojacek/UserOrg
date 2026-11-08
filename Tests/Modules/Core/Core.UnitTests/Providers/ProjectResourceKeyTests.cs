using Core.Domain.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for <see cref="ProjectResourceKey"/>, the natural identifier <c>(ProviderName, ResourceName)</c>
/// (ADR-0041 §1). The same name under different providers is a distinct resource (ADR-0026), so the key
/// must distinguish them.
/// </summary>
public sealed class ProjectResourceKeyTests
{
    [Fact]
    public void Should_BeEqual_When_SameProviderAndName()
    {
        var left = new ProjectResourceKey(ProviderName.Create("acme"), ResourceName.Create("gpu-res"));
        var right = new ProjectResourceKey(ProviderName.Create("acme"), ResourceName.Create("gpu-res"));

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Should_NotBeEqual_When_SameNameDifferentProvider()
    {
        // The name alone is not unique across providers (ADR-0026) — the provider qualifies it.
        var acme = new ProjectResourceKey(ProviderName.Create("acme"), ResourceName.Create("gpu-res"));
        var other = new ProjectResourceKey(ProviderName.Create("other"), ResourceName.Create("gpu-res"));

        Assert.NotEqual(acme, other);
        Assert.True(acme != other);
    }

    [Fact]
    public void Should_NotBeEqual_When_SameProviderDifferentName()
    {
        var gpu = new ProjectResourceKey(ProviderName.Create("acme"), ResourceName.Create("gpu-res"));
        var cpu = new ProjectResourceKey(ProviderName.Create("acme"), ResourceName.Create("cpu-res"));

        Assert.NotEqual(gpu, cpu);
    }
}
