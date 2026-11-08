using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace UserOrg.Shared.Http;

/// <summary>
/// The internal API-to-gateway header naming the IdP user ids whose memberships or roles a request changed (ADR 0113).
/// </summary>
public static class PermissionsChangedHeader
{
    public const string Name = "X-UserOrg-Permissions-Changed";
    public const string EvictAll = "*";
    public const string CapabilityHeader = "X-UserOrg-Gateway-Capabilities";
    public const string Capability = "permissions-changed";

    private const int MaxIds = 100;
    private const int MaxBytes = 4096;
    private const int MaxIdLength = 255;
    private static readonly object ItemsKey = new();

    /// <summary>
    /// Gathers the ids and writes the header when the response starts, so it survives the exception handler.
    /// Does nothing unless the request carries the gateway capability; above 100 ids or 4 KB it sends <see cref="EvictAll"/>.
    /// </summary>
    public static void Append(HttpContext context, IEnumerable<string> idpUserIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(idpUserIds);

        if (!string.Equals(context.Request.Headers[CapabilityHeader].ToString(), Capability, StringComparison.OrdinalIgnoreCase))
            return;

        if (context.Items[ItemsKey] is not HashSet<string> ids)
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            context.Items[ItemsKey] = ids;
            context.Response.OnStarting(static state =>
            {
                var ctx = (HttpContext)state;
                var gathered = (HashSet<string>)ctx.Items[ItemsKey]!;
                if (gathered.Count > 0)
                    ctx.Response.Headers[Name] = Format(gathered);
                return Task.CompletedTask;
            }, context);
        }

        foreach (var id in idpUserIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }
    }

    /// <summary>
    /// The distinct ids, or <see cref="EvictAll"/> alone; an empty list when the value is malformed.
    /// </summary>
    public static IReadOnlyList<string> Parse(StringValues value)
    {
        if (value.Count != 1)
            return [];

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in value[0]!.Split(','))
        {
            var id = part.Trim();
            if (id.Length == 0 || seen.Contains(id))
                continue;
            if (!IsWellFormed(id))
                return [];
            seen.Add(id);
            ids.Add(id);
        }

        if (ids.Contains(EvictAll) && ids.Count > 1)
            return [];

        return ids;
    }

    private static bool IsWellFormed(string id) =>
        id.Length <= MaxIdLength && !id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    private static string Format(HashSet<string> ids)
    {
        if (ids.Count > MaxIds || ids.Contains(EvictAll))
            return EvictAll;

        var joined = string.Join(',', ids);
        return Encoding.UTF8.GetByteCount(joined) > MaxBytes ? EvictAll : joined;
    }
}