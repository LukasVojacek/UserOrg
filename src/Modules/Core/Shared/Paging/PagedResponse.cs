using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Core.Shared.Paging;

/// <summary>
/// Emits a v2 list response as a bare JSON array with pagination metadata in HTTP response headers
/// (ADR-0020 amendment 2026-06-16): <c>X-Total-Count</c> always, plus an RFC 8288 <c>Link</c> header
/// carrying <c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c> navigation for growable lists.
/// </summary>
internal static class PagedResponse
{
    /// <summary>
    /// One offset page: sets <c>X-Total-Count</c> and (when there is more than the current page) a
    /// <c>Link</c> header, then returns the bare <paramref name="data"/> array. Link URLs preserve the
    /// request's other query parameters (filters) and override only <c>page</c>/<c>limit</c>.
    /// </summary>
    public static IResult Offset<T>(HttpContext http, IReadOnlyList<T> data, int page, int limit, int totalItems)
    {
        http.Response.Headers["X-Total-Count"] = totalItems.ToString();

        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)limit);
        var links = new List<string>();
        if (totalPages > 0)
        {
            var parameters = QueryHelpers.ParseQuery(http.Request.QueryString.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            if (page > 1)
            {
                links.Add(Rel(http, parameters, 1, limit, "first"));
                links.Add(Rel(http, parameters, page - 1, limit, "prev"));
            }

            if (page < totalPages)
            {
                links.Add(Rel(http, parameters, page + 1, limit, "next"));
                links.Add(Rel(http, parameters, totalPages, limit, "last"));
            }
        }

        if (links.Count > 0)
            http.Response.Headers.Link = string.Join(", ", links);

        return Results.Ok(data);
    }

    /// <summary>
    /// A bounded reference list that is complete in one page: sets <c>X-Total-Count</c> to the item count,
    /// emits no <c>Link</c> (there is no next/prev), and returns the bare <paramref name="data"/> array.
    /// </summary>
    public static IResult Complete<T>(HttpContext http, IReadOnlyList<T> data)
    {
        http.Response.Headers["X-Total-Count"] = data.Count.ToString();
        return Results.Ok(data);
    }

    private static string Rel(HttpContext http, Dictionary<string, StringValues> parameters, int page, int limit, string rel) =>
        $"<{BuildUrl(http, parameters, page, limit)}>; rel=\"{rel}\"";

    private static string BuildUrl(HttpContext http, Dictionary<string, StringValues> parameters, int page, int limit)
    {
        parameters["page"] = new StringValues(page.ToString());
        parameters["limit"] = new StringValues(limit.ToString());

        var path = http.Request.PathBase.Add(http.Request.Path).ToString();
        var query = parameters.SelectMany(kv => kv.Value, (kv, v) => new KeyValuePair<string, string?>(kv.Key, v));
        return $"{http.Request.Scheme}://{http.Request.Host}{QueryHelpers.AddQueryString(path, query)}";
    }
}