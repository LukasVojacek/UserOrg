using System.Text.Json;

using Core.DataAccess.Exceptions;
using Core.Domain.Exceptions;

using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrganizationManagement.API;
using UserOrganizationManagement.Domain.Exceptions;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Characterization tests pinning the CURRENT status/title/extensions produced by every existing
/// <see cref="GlobalExceptionHandler"/> branch, taken against unchanged source. This is a safety net
/// for future switch edits (issue #260): a failure here means the handler's contract changed and
/// must be justified, not that the test is stale or needs updating. The suite deliberately does NOT
/// pin logger calls, so legitimate logging fixes are free to change. Located here because
/// GlobalExceptionHandler is internal to the API assembly.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Should_Return_400_ValidationFailed_When_ValidationException()
    {
        var failures = new List<ValidationFailure>
        {
            new("Name", "Name is required"),
        };
        var (handled, context, body, _) = await InvokeAsync(new ValidationException(failures));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, body.GetProperty("status").GetInt32());
        Assert.Equal("Validation Failed", body.GetProperty("title").GetString());
        JsonElement errors = body.GetProperty("errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        JsonElement firstError = errors[0];
        // Casing is PascalCase, not the camelCase JsonOptions default: the app configures
        // PropertyNamingPolicy = null (HostInfrastructureExtensions.cs:81-88), which InvokeAsync
        // below reproduces via context.RequestServices so this pins what the wire really sends.
        Assert.Equal("Name", firstError.GetProperty("PropertyName").GetString());
        Assert.Equal("Name is required", firstError.GetProperty("ErrorMessage").GetString());
    }

    [Fact]
    public async Task Should_Return_400_AuthorizationTokenNotFound_When_BearerTokenNotFoundException()
    {
        var (handled, context, body, _) = await InvokeAsync(new BearerTokenNotFoundException("no token"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, body.GetProperty("status").GetInt32());
        Assert.Equal("Authorization token not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_400_ValidationInvalidFormat_When_BadHttpRequestException()
    {
        const string bindingMessage = "Failed to bind parameter \"ProjectShortName\" from \" \".";
        var (handled, context, body, _) = await InvokeAsync(new BadHttpRequestException(bindingMessage));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, body.GetProperty("status").GetInt32());
        Assert.Equal("Validation failed", body.GetProperty("title").GetString());
        Assert.Equal("One or more request parameters have an invalid format.", body.GetProperty("detail").GetString());
        Assert.Equal("validation.invalid_format", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("requestId").GetString()));
        // ADR-0020: exception.Message is logged, never returned — the raw binding text must not leak
        // into any string value of the body (not just "detail"). Checked on decoded string values,
        // not GetRawText(): the web JSON encoder escapes '"', so a raw-text substring match would
        // never see a quote-containing leak even if the handler echoed the exception message.
        AssertNoStringValueContains(body, "Failed to bind parameter");
    }

    [Fact]
    public async Task Should_Return_404_RecordNotFound_When_RecordNotFoundException()
    {
        var (handled, context, body, _) = await InvokeAsync(new RecordNotFoundException("missing"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, body.GetProperty("status").GetInt32());
        Assert.Equal("Record not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_404_ProjectNotFound_When_ProjectNotFoundException()
    {
        var (handled, context, body, _) = await InvokeAsync(new ProjectNotFoundException("missing"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, body.GetProperty("status").GetInt32());
        Assert.Equal("Project not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_404_UserNotFound_When_UserNotFoundException()
    {
        var (handled, context, body, _) = await InvokeAsync(new UserNotFoundException("missing"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, body.GetProperty("status").GetInt32());
        Assert.Equal("User not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_404_RoleNotFound_When_SystemRoleNotFoundException()
    {
        var (handled, context, body, _) = await InvokeAsync(new SystemRoleNotFoundException("missing"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, body.GetProperty("status").GetInt32());
        Assert.Equal("Role not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_404_SystemPermissionTypeNotFound_When_SystemPermissionTypeNotFoundException()
    {
        var (handled, context, body, _) = await InvokeAsync(new SystemPermissionTypeNotFoundException("missing"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, body.GetProperty("status").GetInt32());
        Assert.Equal("System permission type not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_409_RecordReadOnly_When_RecordReadOnlyException()
    {
        var (handled, context, body, _) = await InvokeAsync(new RecordReadOnlyException());

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, body.GetProperty("status").GetInt32());
        Assert.Equal("Record is read-only", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_502_RoleServiceUnavailable_When_RoleServiceException()
    {
        var (handled, context, body, _) = await InvokeAsync(new RoleServiceException());

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status502BadGateway, body.GetProperty("status").GetInt32());
        Assert.Equal("Role service unavailable", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_Return_503_HeappeServiceUnavailable_WithoutFailures_When_HeappeUnavailableExceptionHasNoFailures()
    {
        var (handled, context, body, _) = await InvokeAsync(new HeappeUnavailableException("unavailable"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, body.GetProperty("status").GetInt32());
        Assert.Equal("HEAppE service unavailable", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("failures", out _));
    }

    [Fact]
    public async Task Should_Return_503_HeappeServiceUnavailable_WithFailures_When_HeappeUnavailableExceptionHasFailures()
    {
        var failures = new List<HeappeFailureInfo> { new("instance-1", "timeout") };
        var (handled, context, body, _) = await InvokeAsync(new HeappeUnavailableException("unavailable", failures));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, body.GetProperty("status").GetInt32());
        JsonElement reportedFailures = body.GetProperty("failures");
        JsonElement firstFailure = reportedFailures[0];
        Assert.Equal("instance-1", firstFailure.GetProperty("instance").GetString());
        Assert.Equal("timeout", firstFailure.GetProperty("cause").GetString());
    }

    [Fact]
    public async Task Should_Return_500_InternalErrorBuildingTemplatesInformation_When_TemplatesInfoPreconditionException()
    {
        var (handled, context, body, _) = await InvokeAsync(new TemplatesInfoPreconditionException("bad precondition"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, body.GetProperty("status").GetInt32());
        Assert.Equal("Internal error building templates information", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Should_DeclineHandling_WithEmptyBody_When_TaskCanceledException()
    {
        var (handled, context, _, bodyLength) = await InvokeAsync(new TaskCanceledException(), readBody: false);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, bodyLength);
    }

    [Fact]
    public async Task Should_DeclineHandling_WithEmptyBody_When_OperationCanceledException()
    {
        var (handled, context, _, bodyLength) = await InvokeAsync(new OperationCanceledException(), readBody: false);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, bodyLength);
    }

    [Fact]
    public async Task Should_Return_409_RecordConflict_When_DbUpdateConcurrencyException()
    {
        var (handled, context, body, _) = await InvokeAsync(new DbUpdateConcurrencyException());

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Should_Return_500_InternalError_When_UnmappedException()
    {
        var (handled, context, body, _) = await InvokeAsync(new InvalidOperationException("boom"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, body.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("title").GetString());
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
    }

    [Theory]
    [MemberData(nameof(HandledExceptions))]
    public async Task Should_SetRequestId_When_ExceptionIsHandled(Exception exception)
    {
        var (handled, _, body, _) = await InvokeAsync(exception);

        Assert.True(handled);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("requestId").GetString()));
    }

    public static TheoryData<Exception> HandledExceptions() => new()
    {
        new ValidationException([new ValidationFailure("Name", "Name is required")]),
        new BearerTokenNotFoundException("no token"),
        new BadHttpRequestException("Failed to bind parameter \"ProjectShortName\" from \" \"."),
        new RecordNotFoundException("missing"),
        new ProjectNotFoundException("missing"),
        new UserNotFoundException("missing"),
        new SystemRoleNotFoundException("missing"),
        new SystemPermissionTypeNotFoundException("missing"),
        new RecordReadOnlyException(),
        new DbUpdateConcurrencyException(),
        new RoleServiceException(),
        new HeappeUnavailableException("unavailable"),
        new TemplatesInfoPreconditionException("bad precondition"),
        new InvalidOperationException("boom"),
    };

    private static async Task<(bool Handled, HttpContext Context, JsonElement Body, long BodyLength)> InvokeAsync(
        Exception exception, bool readBody = true)
    {
        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        // WriteAsJsonAsync resolves JsonOptions from RequestServices; without this it falls back to
        // ASP.NET's own camelCase default instead of the app's actual configuration, which would
        // pin a wire shape production never emits. Mirrors HostInfrastructureExtensions.cs:81-88.
        using ServiceProvider jsonOptionsProvider = BuildJsonOptionsProviderMatchingProduction();
        context.RequestServices = jsonOptionsProvider;

        bool handled = await sut.TryHandleAsync(context, exception, CancellationToken.None);

        if (!readBody)
        {
            return (handled, context, default, responseBody.Length);
        }

        responseBody.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseBody);
        string json = await reader.ReadToEndAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        return (handled, context, document.RootElement.Clone(), responseBody.Length);
    }

    private static ServiceProvider BuildJsonOptionsProviderMatchingProduction()
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        return services.BuildServiceProvider();
    }

    private static void AssertNoStringValueContains(JsonElement element, string forbiddenSubstring)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                using (JsonElement.ObjectEnumerator properties = element.EnumerateObject())
                {
                    foreach (JsonProperty property in properties)
                    {
                        AssertNoStringValueContains(property.Value, forbiddenSubstring);
                    }
                }

                break;
            case JsonValueKind.Array:
                using (JsonElement.ArrayEnumerator items = element.EnumerateArray())
                {
                    foreach (JsonElement item in items)
                    {
                        AssertNoStringValueContains(item, forbiddenSubstring);
                    }
                }

                break;
            case JsonValueKind.String:
                Assert.DoesNotContain(forbiddenSubstring, element.GetString(), StringComparison.Ordinal);
                break;
        }
    }
}
