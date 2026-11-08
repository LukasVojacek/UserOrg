using Core.Shared.Http;

using Microsoft.AspNetCore.Http;

namespace Core.UnitTests.Shared;

public sealed class ConditionalGetMiddlewareTests
{
    private static async Task<(DefaultHttpContext Context, string Body)> RunAsync(
        string method = "GET",
        bool withETagMetadata = true,
        string? ifNoneMatch = null,
        int statusCode = StatusCodes.Status200OK,
        string payload = "payload")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (ifNoneMatch is not null)
            context.Request.Headers.IfNoneMatch = ifNoneMatch;
        if (withETagMetadata)
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ETagMetadata()), "test"));

        var sink = new MemoryStream();
        context.Response.Body = sink;

        RequestDelegate next = async ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            if (payload.Length > 0)
                await ctx.Response.WriteAsync(payload);
        };

        await new ConditionalGetMiddleware(next).InvokeAsync(context);

        sink.Position = 0;
        var body = await new StreamReader(sink).ReadToEndAsync();
        return (context, body);
    }

    [Fact]
    public async Task Should_EmitStrongETag_AndNoCache_When_GetWithMetadataReturns200()
    {
        var (context, body) = await RunAsync();

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("payload", body);
        var etag = context.Response.Headers.ETag.ToString();
        Assert.StartsWith("\"", etag);
        Assert.EndsWith("\"", etag);
        Assert.Equal("private, no-cache", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Should_Return304_AndEmptyBody_When_IfNoneMatchMatches()
    {
        var etag = (await RunAsync().ConfigureAwait(false)).Context.Response.Headers.ETag.ToString();

        var (context, body) = await RunAsync(ifNoneMatch: etag);

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
        Assert.Empty(body);
        Assert.False(context.Response.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task Should_Return200_AndBody_When_IfNoneMatchDoesNotMatch()
    {
        var (context, body) = await RunAsync(ifNoneMatch: "\"deadbeef\"");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("payload", body);
        Assert.False(string.IsNullOrEmpty(context.Response.Headers.ETag.ToString()));
    }

    [Fact]
    public async Task Should_Return304_When_IfNoneMatchEchoesWeakValidator()
    {
        var etag = (await RunAsync().ConfigureAwait(false)).Context.Response.Headers.ETag.ToString();

        var (context, _) = await RunAsync(ifNoneMatch: $"W/{etag}");

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
    }

    [Fact]
    public async Task Should_Return304_When_IfNoneMatchIsWildcard()
    {
        var (context, _) = await RunAsync(ifNoneMatch: "*");

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
    }

    [Fact]
    public async Task Should_NotSetETag_When_EndpointHasNoMetadata()
    {
        var (context, body) = await RunAsync(withETagMetadata: false);

        Assert.Equal("payload", body);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task Should_NotSetETag_When_MethodIsNotGet()
    {
        var (context, body) = await RunAsync(method: "POST");

        Assert.Equal("payload", body);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task Should_NotSetETag_When_StatusIsNot200()
    {
        var (context, body) = await RunAsync(statusCode: StatusCodes.Status404NotFound);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("payload", body);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
    }
}
