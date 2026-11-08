using System.Text;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests;

public sealed class RoleManagementHtmlExportTests
{

    [Fact]
    public async Task GetRbacHtml_ReturnsEmptyHtmlOnConfigError()
    {
        var mock = new Mock<IRoleManagementService>();
        mock.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ConnectionString not initialized"));

        var result = await RoleManagementExportApi.GetRbacHtmlAsync(mock.Object, CancellationToken.None);
        Assert.NotNull(result);

        var context = new DefaultHttpContext();
        using var ms = new MemoryStream();
        context.Response.Body = ms;
        await result.ExecuteAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("text/html", context.Response.ContentType);
    }

    [Fact]
    public async Task GetRbacHtml_PutEndpoint_IncludesMethodBadgeAndPermissionsRow()
    {
        var mock = new Mock<IRoleManagementService>();
        var endpoint = new EndpointMappingInfo
        {
            EndpointName = "AcceptRequest",
            PathTemplate = "/api/Requests/{RequestId}/Accept",
            HttpMethod = "PUT",
            Permissions = new[] { "AcceptRequest" }
        };

        mock.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(new[] { endpoint }));

        // No roles defined for simplicity
        mock.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success(Array.Empty<RoleSummary>()));

        var result = await RoleManagementExportApi.GetRbacHtmlAsync(mock.Object, CancellationToken.None);
        Assert.NotNull(result);

        var context = new DefaultHttpContext();
        using var ms = new MemoryStream();
        context.Response.Body = ms;
        await result.ExecuteAsync(context);

        ms.Position = 0;
        var html = new StreamReader(ms, Encoding.UTF8).ReadToEnd();

        Assert.Contains("method-put", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">PUT<", html, System.StringComparison.OrdinalIgnoreCase);

        Assert.Contains("<code class=\"perm\">AcceptRequest</code>", html);

        // Ensure the permission appears after the endpoint row
        var idxEndpoint = html.IndexOf("/api/Requests/{RequestId}/Accept", System.StringComparison.OrdinalIgnoreCase);
        Assert.True(idxEndpoint >= 0, "Endpoint path not found in HTML");
        var idxPerm = html.IndexOf("<code class=\"perm\">AcceptRequest</code>", System.StringComparison.OrdinalIgnoreCase);
        Assert.True(idxPerm > idxEndpoint, "Permission should appear after endpoint row");

        // Zebra effect: at least one inline background style should be present on rows
        Assert.True(html.Contains("background:#f9f9f9") || html.Contains("class=\"row-"), "Expected zebra row styling or classes");
    }
}