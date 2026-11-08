using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;

namespace Core.UnitTests.Cache;

public sealed class CoreCachePolicyTests
{
    private static readonly CacheDescriptor Descriptor = new(
        TimeSpan.FromMinutes(7),
        new Dictionary<string, string> { ["sub"] = "user-1", ["page"] = "2" },
        ["project:PROJ-1", "resource:r-1"]);

    private static OutputCacheContext NewContext(int statusCode = StatusCodes.Status200OK)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = statusCode;
        return new OutputCacheContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task Should_DisableCachingAndAddNoTags_When_DescriptorIsNull()
    {
        var context = NewContext();
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(null));

        await policy.CacheRequestAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.EnableOutputCaching);
        Assert.False(context.AllowCacheLookup);
        Assert.False(context.AllowCacheStorage);
        Assert.False(context.AllowLocking);
        Assert.Empty(context.Tags);
        Assert.Empty(context.CacheVaryByRules.VaryByValues);
    }

    [Fact]
    public async Task Should_EnableCachingWithTheDescriptor_When_DescriptorIsReturned()
    {
        var context = NewContext();
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(Descriptor));

        await policy.CacheRequestAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.EnableOutputCaching);
        Assert.True(context.AllowCacheLookup);
        Assert.True(context.AllowCacheStorage);
        Assert.True(context.AllowLocking);
        Assert.Equal(TimeSpan.FromMinutes(7), context.ResponseExpirationTimeSpan);
        Assert.Equal(["all", "project:PROJ-1", "resource:r-1"], context.Tags.Order(StringComparer.Ordinal));
        Assert.Equal("user-1", context.CacheVaryByRules.VaryByValues["sub"]);
        Assert.Equal("2", context.CacheVaryByRules.VaryByValues["page"]);
        Assert.Equal(2, context.CacheVaryByRules.VaryByValues.Count);
    }

    [Fact]
    public async Task Should_AddOnlyTheGlobalTag_When_DescriptorHasNoTagsOrVaryBy()
    {
        var context = NewContext();
        var empty = new CacheDescriptor(TimeSpan.FromSeconds(30), new Dictionary<string, string>(), []);
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(empty));

        await policy.CacheRequestAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["all"], context.Tags);
        Assert.Empty(context.CacheVaryByRules.VaryByValues);
    }

    [Fact]
    public async Task Should_PassTheRequestAndToken_When_DescribingTheRequest()
    {
        var context = NewContext();
        using var cts = new CancellationTokenSource();
        HttpContext? seenContext = null;
        CancellationToken seenToken = default;
        var policy = new CoreCachePolicy((http, ct) =>
        {
            seenContext = http;
            seenToken = ct;
            return ValueTask.FromResult<CacheDescriptor?>(null);
        });

        await policy.CacheRequestAsync(context, cts.Token);

        Assert.Same(context.HttpContext, seenContext);
        Assert.Equal(cts.Token, seenToken);
    }

    [Fact]
    public async Task Should_StoreAndAddTheResponseTags_When_StatusIs200AndStorageIsNotSuppressed()
    {
        var context = NewContext();
        ResponseCacheTags.Add(context.HttpContext, ["location:gpu", "project:PROJ-2"]);
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(null));

        await policy.ServeResponseAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.AllowCacheStorage);
        Assert.Equal(["location:gpu", "project:PROJ-2"], context.Tags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Should_StoreWithNoExtraTags_When_StatusIs200AndNoResponseTagsWereAdded()
    {
        var context = NewContext();
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(null));

        await policy.ServeResponseAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.AllowCacheStorage);
        Assert.Empty(context.Tags);
    }

    [Fact]
    public async Task Should_NotStoreOrAddTags_When_StorageIsSuppressed()
    {
        var context = NewContext();
        context.AllowCacheStorage = true;
        ResponseCacheTags.Add(context.HttpContext, ["location:gpu"]);
        ResponseCacheControl.SuppressStorage(context.HttpContext);
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(null));

        await policy.ServeResponseAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.AllowCacheStorage);
        Assert.Empty(context.Tags);
    }

    [Theory]
    [InlineData(StatusCodes.Status201Created)]
    [InlineData(StatusCodes.Status304NotModified)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task Should_NotStoreOrAddTags_When_StatusIsNot200(int statusCode)
    {
        var context = NewContext(statusCode);
        context.AllowCacheStorage = true;
        ResponseCacheTags.Add(context.HttpContext, ["location:gpu"]);
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(null));

        await policy.ServeResponseAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.AllowCacheStorage);
        Assert.Empty(context.Tags);
    }

    [Fact]
    public async Task Should_ChangeNothing_When_ServingFromCache()
    {
        var context = NewContext();
        var policy = new CoreCachePolicy((_, _) => ValueTask.FromResult<CacheDescriptor?>(Descriptor));

        await policy.ServeFromCacheAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.EnableOutputCaching);
        Assert.False(context.AllowCacheStorage);
        Assert.Empty(context.Tags);
    }
}
