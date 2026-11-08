using System.Security.Cryptography;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.Primitives;

namespace Core.Shared.Http;

/// <summary>
/// Adds client-side conditional-GET support to endpoints marked with <see cref="ETagMetadata"/>.
/// Registered in front of the output-cache middleware, so it revalidates responses served from
/// the output cache too: it computes a strong ETag from the (possibly cached) response body and
/// turns a matching <c>If-None-Match</c> into <c>304 Not Modified</c>.
///
/// Responses are tagged <c>Cache-Control: private, no-cache</c> — the data is per-user and behind
/// a bearer token, so it must never land in a shared cache, and <c>no-cache</c> forces the client
/// to revalidate every time. A permission change therefore yields a new body hash and is reflected
/// immediately rather than after a TTL.
/// </summary>
internal sealed class ConditionalGetMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || context.GetEndpoint()?.Metadata.GetMetadata<ETagMetadata>() is null)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            buffer.Position = 0;
            if (context.Response.StatusCode != StatusCodes.Status200OK || buffer.Length == 0)
            {
                await buffer.CopyToAsync(originalBody, context.RequestAborted);
                return;
            }

            var etag = ComputeETag(buffer);
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "private, no-cache";

            if (Matches(context.Request.Headers.IfNoneMatch, etag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                context.Response.Headers.ContentType = default;
                return;
            }

            buffer.Position = 0;
            context.Response.ContentLength = buffer.Length;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static string ComputeETag(Stream body)
        => $"\"{Convert.ToHexStringLower(SHA256.HashData(body))}\"";

    private static bool Matches(StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (header is null)
            {
                continue;
            }

            foreach (var candidate in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (candidate == "*")
                {
                    return true;
                }

                // Strong-compare, ignoring a weak validator prefix the client may echo back.
                var normalized = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
                if (string.Equals(normalized, etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public static class ConditionalGetApplicationBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="ConditionalGetMiddleware"/>. Call immediately before
    /// <c>UseOutputCache()</c> so responses served from the output cache are revalidated as well.
    /// </summary>
    public static IApplicationBuilder UseCoreConditionalGet(this IApplicationBuilder app)
        => app.UseMiddleware<ConditionalGetMiddleware>();
}