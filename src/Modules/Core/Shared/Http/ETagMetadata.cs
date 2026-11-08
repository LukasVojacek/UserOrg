using Microsoft.AspNetCore.Builder;

namespace Core.Shared.Http;

/// <summary>
/// Marks an endpoint as participating in client-side conditional GET. The
/// <see cref="ConditionalGetMiddleware"/> emits a strong <c>ETag</c> over the response body and
/// answers a matching <c>If-None-Match</c> with <c>304 Not Modified</c>. Only GET endpoints
/// carrying this metadata are buffered/hashed; everything else passes through untouched.
/// </summary>
internal sealed class ETagMetadata;

internal static class ETagEndpointExtensions
{
    extension(RouteHandlerBuilder builder)
    {
        /// <summary>Enable body-hash ETag + 304 revalidation for this endpoint.</summary>
        internal RouteHandlerBuilder WithETag() => builder.WithMetadata(new ETagMetadata());
    }
}