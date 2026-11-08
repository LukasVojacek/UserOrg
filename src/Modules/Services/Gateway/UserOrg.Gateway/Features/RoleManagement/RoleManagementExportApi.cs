using System.Text;

using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Features.RoleManagement;

internal static class RoleManagementExportApi
{
    /// <summary>Message fragments, matched ignoring case, that mark an exception as a configuration error.</summary>
    private static readonly string[] ConfigurationErrorMarkers =
        ["connectionstring", "connection string", "not initialized", "not configured", "missing configuration"];

    public static async Task<IResult> GetRbacHtmlAsync(IRoleManagementService service, CancellationToken cancellationToken)
    {
        if (service == null)
        {
            var message = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><p>RBAC export is unavailable: role-management service is not configured.</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(message);
            return new HtmlAttachmentResult(bytes, "rbac-matrix.html");
        }
        try
        {
            var entries = await RoleManagementApi.BuildRbacAsync(service, cancellationToken);
            var html = BuildRbacHtml(entries);
            var bytes = Encoding.UTF8.GetBytes(html);
            return new HtmlAttachmentResult(bytes, "rbac-matrix.html");
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
        catch (Exception ex)
        {
            if (IsConfigurationError(ex))
            {
                var message = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><p>RBAC export is unavailable: missing configuration.</p></body></html>";
                var bytes = Encoding.UTF8.GetBytes(message);
                return new HtmlAttachmentResult(bytes, "rbac-matrix.html");
            }
            return Results.Problem(title: "Failed to generate RBAC export", detail: ex.Message);
        }

    }

    /// <summary>
    /// True when the exception or one of its inner exceptions is an <see cref="InvalidOperationException"/>,
    /// an <see cref="ArgumentException"/>, or carries a message naming missing configuration.
    /// </summary>
    private static bool IsConfigurationError(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is InvalidOperationException or ArgumentException || MentionsMissingConfiguration(current.Message))
                return true;
        }

        return false;
    }

    private static bool MentionsMissingConfiguration(string message)
        => Array.Exists(ConfigurationErrorMarkers, marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string BuildRbacHtml(IEnumerable<RbacEntry> entries) => RbacHtmlDocument.Render(entries);

    private sealed class HtmlAttachmentResult : IResult
    {
        private readonly byte[] _content;
        private readonly string _fileName;

        public HtmlAttachmentResult(byte[] content, string fileName)
        {
            _content = content ?? Array.Empty<byte>();
            _fileName = fileName ?? "rbac-matrix.html";
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentType = "text/html";
            httpContext.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{_fileName}\"";
            await httpContext.Response.Body.WriteAsync(_content, 0, _content.Length, CancellationToken.None);
        }
    }
}