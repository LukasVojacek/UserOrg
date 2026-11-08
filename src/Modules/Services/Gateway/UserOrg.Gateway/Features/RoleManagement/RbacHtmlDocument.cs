using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;

namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Renders the RBAC matrix HTML document of the exports: one table row per endpoint and, for a <c>PUT</c>
/// endpoint, a second row listing its permissions.
/// </summary>
internal static class RbacHtmlDocument
{
    private const string EmptyCell = "—";

    private const string StyleSheet = "body{font-family:Segoe UI, Arial, sans-serif;padding:20px;} .rbac-table{border-collapse:collapse;width:100%;} .rbac-table th,.rbac-table td{border:1px solid #ddd;padding:8px;vertical-align:top;} .rbac-table th{background:#f4f4f4;text-align:left;} .rbac-table tr:nth-child(even){background:#f9f9f9;} .rbac-table tr:hover{background:#f1f7ff;} td{text-align:left;} .endpoint{font-weight:600;} .method{font-weight:700;padding:3px 8px;border-radius:6px;margin-right:8px;font-size:0.85em;display:inline-block;} .method-get{background:#2e7d32;color:#ffffff;} .method-post{background:#1565c0;color:#ffffff;} .method-put{background:#ffb300;color:#000000;} .method-delete{background:#d32f2f;color:#ffffff;} .perm-list{font-size:0.9em;color:#333;margin-top:6px;display:block;} code.perm{background:#f4f4f4;padding:4px 6px;border-radius:4px;margin-right:6px;font-family:Menlo,Monaco,Consolas,'Courier New',monospace;white-space:normal;word-break:break-word;display:inline-block;max-width:100%;} @media print{ .method{box-shadow:none;} .rbac-table th, .rbac-table td{page-break-inside:avoid;} }";

    /// <summary>Renders the whole document with the RBAC matrix style sheet.</summary>
    public static string Render(IEnumerable<RbacEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>");
        sb.Append(StyleSheet);
        sb.Append("</style></head><body>");
        sb.Append("<h1>RBAC Matrix</h1>");
        sb.Append("<table class=\"rbac-table\">");
        sb.Append("<thead><tr><th>Endpoint</th><th>Allowed Roles</th></tr></thead>");
        sb.Append("<tbody>");

        foreach (var entry in entries)
        {
            AppendEntry(sb, entry);
        }

        sb.Append("</tbody></table>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendEntry(StringBuilder sb, RbacEntry entry)
    {
        var roles = RolesText(entry.Roles);
        var httpMethod = entry.HttpMethod ?? string.Empty;
        var methodSpan = MethodSpan(httpMethod);
        var endpointPath = WebUtility.HtmlEncode(entry.Endpoint ?? string.Empty);
        sb.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"endpoint\">{methodSpan}{endpointPath}</td><td>{WebUtility.HtmlEncode(roles)}</td></tr>");

        if (string.Equals(httpMethod, "PUT", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(CultureInfo.InvariantCulture, $"<tr class=\"perm-row\"><td colspan=\"2\" class=\"perm-list\">Permissions: {PermissionsText(entry.Permissions)}</td></tr>");
        }
    }

    private static string RolesText(string[]? roles) =>
        roles is { Length: > 0 } ? string.Join(", ", roles) : EmptyCell;

    private static string PermissionsText(string[]? permissions) =>
        permissions is { Length: > 0 }
            ? string.Join(", ", permissions.Select(p => $"<code class=\"perm\">{WebUtility.HtmlEncode(p)}</code>"))
            : EmptyCell;

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The method CSS classes (method-get, method-put, ...) are lower case; this is not security normalization.")]
    private static string MethodSpan(string httpMethod)
    {
        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            return string.Empty;
        }

        var cls = $"method-{WebUtility.HtmlEncode(httpMethod.ToLowerInvariant())}";
        return $"<span class=\"method {cls}\">{WebUtility.HtmlEncode(httpMethod.ToUpperInvariant())}</span>";
    }
}