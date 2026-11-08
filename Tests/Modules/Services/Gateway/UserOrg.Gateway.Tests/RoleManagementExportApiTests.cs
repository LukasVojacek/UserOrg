using System.Text;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the RBAC HTML export's failure answers: a missing service and every exception the export
/// classifies as a configuration problem (an <see cref="InvalidOperationException"/> or
/// <see cref="ArgumentException"/>, a message naming a connection string or a missing/uninitialized
/// configuration, at any depth of inner exceptions) still download an explanatory HTML page; a cancellation
/// answers 499; anything else is a 500 problem carrying the exception message.
/// </summary>
public sealed class RoleManagementExportApiTests
{
    private const string FileName = "rbac-matrix.html";
    private const string MissingConfigurationPage = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><p>RBAC export is unavailable: missing configuration.</p></body></html>";

    [Fact]
    public async Task Should_DownloadTheServiceNotConfiguredPage_When_NoServiceIsAvailable()
    {
        var result = await RoleManagementExportApi.GetRbacHtmlAsync(null!, TestContext.Current.CancellationToken);

        var (context, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("<!doctype html><html><head><meta charset=\"utf-8\"></head><body><p>RBAC export is unavailable: role-management service is not configured.</p></body></html>", body);
    }

    [Fact]
    public async Task Should_DownloadTheMatrixAsAnHtmlAttachment_When_TheExportSucceeds()
    {
        var service = SucceedingService();

        var result = await RoleManagementExportApi.GetRbacHtmlAsync(service, TestContext.Current.CancellationToken);

        var (context, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("text/html", context.Response.ContentType);
        Assert.Equal($"attachment; filename=\"{FileName}\"", context.Response.Headers.ContentDisposition.ToString());
        Assert.StartsWith("<!doctype html>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_DownloadTheMissingConfigurationPage_When_TheMappingsCannotBeRead()
    {
        var service = new Mock<IRoleManagementService>();
        service.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Failure(new Error("x", ErrorSeverity.Error, "x")));

        var result = await RoleManagementExportApi.GetRbacHtmlAsync(service.Object, TestContext.Current.CancellationToken);

        var (context, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(MissingConfigurationPage, body);
    }

    public static TheoryData<Exception> ConfigurationErrors() => new()
    {
        new InvalidOperationException("anything"),
        new ArgumentException("anything"),
        new IOException("The ConnectionString property has not been initialized"),
        new IOException("missing CONNECTION STRING for db"),
        new IOException("service not initialized"),
        new IOException("feature Not Configured"),
        new IOException("missing configuration: Foo"),
        new IOException("outer", new InvalidOperationException("inner")),
        new IOException("outer", new IOException("middle", new IOException("store not configured"))),
    };

    [Theory]
    [MemberData(nameof(ConfigurationErrors))]
    public async Task Should_DownloadTheMissingConfigurationPage_When_TheExportFailsWithAConfigurationError(Exception failure)
    {
        var result = await RoleManagementExportApi.GetRbacHtmlAsync(Throwing(failure), TestContext.Current.CancellationToken);

        var (context, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("text/html", context.Response.ContentType);
        Assert.Equal(MissingConfigurationPage, body);
    }

    public static TheoryData<Exception> OtherErrors() => new()
    {
        new IOException("disk full"),
        new IOException("outer", new IOException("inner timeout")),
        new NotSupportedException("nope"),
    };

    [Theory]
    [MemberData(nameof(OtherErrors))]
    public async Task Should_AnswerAProblemWithTheMessage_When_TheExportFailsWithAnyOtherError(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var result = await RoleManagementExportApi.GetRbacHtmlAsync(Throwing(failure), TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.Equal("Failed to generate RBAC export", problem.ProblemDetails.Title);
        Assert.Equal(failure.Message, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_Answer499_When_TheExportIsCancelled()
    {
        var result = await RoleManagementExportApi.GetRbacHtmlAsync(Throwing(new OperationCanceledException()), TestContext.Current.CancellationToken);

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(499, status.StatusCode);
    }

    private static IRoleManagementService Throwing(Exception failure)
    {
        var service = new Mock<IRoleManagementService>();
        service.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        return service.Object;
    }

    private static IRoleManagementService SucceedingService()
    {
        var service = new Mock<IRoleManagementService>();
        service.Setup(s => s.GetEndpointMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success([new EndpointMappingInfo { EndpointName = "ep", PathTemplate = "/api/x", HttpMethod = "GET", Permissions = ["perm:x"] }]));
        service.Setup(s => s.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<RoleSummary>, Error>.Success([]));
        return service.Object;
    }

    private static async Task<(DefaultHttpContext Context, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        using var stream = new MemoryStream();
        context.Response.Body = stream;
        await result.ExecuteAsync(context);
        return (context, Encoding.UTF8.GetString(stream.ToArray()));
    }
}
