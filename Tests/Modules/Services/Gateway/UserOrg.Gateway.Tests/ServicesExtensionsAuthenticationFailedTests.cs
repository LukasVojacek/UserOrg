using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Startup;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Covers the #293 split in <see cref="ServicesExtensions.CreateJwtBearerEvents"/>: <c>OnAuthenticationFailed</c>
/// only classifies the failure into <see cref="HttpContext.Items"/>, and <c>OnChallenge</c> renders it as an
/// RFC 9457 body with a machine-readable <c>code</c>.
/// </summary>
public sealed class ServicesExtensionsAuthenticationFailedTests
{
    private const string Authority = "https://idp.test";

    [Fact]
    public async Task OnAuthenticationFailed_WithExpiredToken_StoresExpiredClassification()
    {
        var (httpContext, _) = BuildHttpContext();
        var context = BuildFailedContext(httpContext, new SecurityTokenExpiredException("expired"));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnAuthenticationFailed(context);

        var failure = Assert.IsType<AuthenticationFailure>(httpContext.Items[ProblemResponse.AuthenticationFailureItemKey]);
        Assert.Equal(StatusCodes.Status401Unauthorized, failure.Status);
        Assert.Equal("auth.token_expired", failure.Code);
    }

    [Fact]
    public async Task OnChallenge_AfterExpiredToken_WritesProblemDetailsWithExpiredCode()
    {
        var (httpContext, body) = BuildHttpContext();
        var events = ServicesExtensions.CreateJwtBearerEvents(Authority);
        await events.OnAuthenticationFailed(BuildFailedContext(httpContext, new SecurityTokenExpiredException("expired")));
        var challenge = BuildChallengeContext(httpContext);

        await events.OnChallenge(challenge);

        Assert.True(challenge.Handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Equal(ProblemResponse.ContentType, httpContext.Response.ContentType);
        Assert.Equal("Bearer error=\"invalid_token\"", httpContext.Response.Headers.WWWAuthenticate.ToString());
        using var problem = ReadProblem(body);
        Assert.Equal("auth.token_expired", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(httpContext.TraceIdentifier, problem.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task OnChallenge_AfterInvalidSignature_Writes422WithInvalidSignatureCode()
    {
        var (httpContext, body) = BuildHttpContext();
        var events = ServicesExtensions.CreateJwtBearerEvents(Authority);
        await events.OnAuthenticationFailed(BuildFailedContext(httpContext, new SecurityTokenInvalidSignatureException("bad signature")));

        await events.OnChallenge(BuildChallengeContext(httpContext));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, httpContext.Response.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("WWW-Authenticate"));
        using var problem = ReadProblem(body);
        Assert.Equal("auth.token_invalid_signature", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OnChallenge_WithNothingStored_Writes401Unauthorized()
    {
        var (httpContext, body) = BuildHttpContext();

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnChallenge(BuildChallengeContext(httpContext));

        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Equal("Bearer", httpContext.Response.Headers.WWWAuthenticate.ToString());
        using var problem = ReadProblem(body);
        Assert.Equal("auth.unauthorized", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OnAuthenticationFailed_Alone_DoesNotStartTheResponse()
    {
        var (httpContext, body) = BuildHttpContext();
        var context = BuildFailedContext(httpContext, new SecurityTokenMalformedException("malformed"));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnAuthenticationFailed(context);

        Assert.False(httpContext.Response.HasStarted);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(0, body.Length);
    }

    [Fact]
    public async Task OnAuthenticationFailed_WithUnmappedSecurityTokenException_DoesNotLeakExceptionMessage()
    {
        var (httpContext, _) = BuildHttpContext();
        var context = BuildFailedContext(httpContext, new SecurityTokenException("internal detail"));

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnAuthenticationFailed(context);

        var failure = Assert.IsType<AuthenticationFailure>(httpContext.Items[ProblemResponse.AuthenticationFailureItemKey]);
        Assert.Equal("auth.token_validation_failed", failure.Code);
        Assert.DoesNotContain("internal detail", failure.Detail, StringComparison.Ordinal);
    }

    public static TheoryData<Exception, int, string> ClassifiedExceptions => new()
    {
        { new HttpRequestException("unreachable"), StatusCodes.Status502BadGateway, "auth.external_service_error" },
        { new OperationCanceledException("cancelled"), StatusCodes.Status503ServiceUnavailable, "auth.service_timeout" },
        { new TaskCanceledException("timed out"), StatusCodes.Status503ServiceUnavailable, "auth.service_timeout" },
        { new SecurityTokenSignatureKeyNotFoundException("no key"), StatusCodes.Status422UnprocessableEntity, "auth.token_signature_unavailable" },
        { new SecurityTokenInvalidSignatureException("bad signature"), StatusCodes.Status422UnprocessableEntity, "auth.token_invalid_signature" },
        { new SecurityTokenMalformedException("malformed"), StatusCodes.Status422UnprocessableEntity, "auth.token_malformed" },
        { new SecurityTokenInvalidAudienceException("audience"), StatusCodes.Status422UnprocessableEntity, "auth.token_invalid_audience" },
        { new SecurityTokenInvalidIssuerException("issuer"), StatusCodes.Status422UnprocessableEntity, "auth.token_invalid_issuer" },
        { new SecurityTokenNoExpirationException("no exp"), StatusCodes.Status422UnprocessableEntity, "auth.token_no_expiration" },
        { new InvalidOperationException("not a token problem"), StatusCodes.Status401Unauthorized, "auth.authentication_failed" },
    };

    [Theory]
    [MemberData(nameof(ClassifiedExceptions))]
    public async Task OnAuthenticationFailed_WithException_StoresMainBranchClassification(Exception exception, int status, string code)
    {
        var (httpContext, _) = BuildHttpContext();

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnAuthenticationFailed(BuildFailedContext(httpContext, exception));

        var failure = Assert.IsType<AuthenticationFailure>(httpContext.Items[ProblemResponse.AuthenticationFailureItemKey]);
        Assert.Equal(status, failure.Status);
        Assert.Equal(code, failure.Code);
    }

    private static (DefaultHttpContext Context, MemoryStream Body) BuildHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Request.Path = "/api/User/Preferences";
        httpContext.Response.Body = body;
        return (httpContext, body);
    }

    private static AuthenticationScheme BuildScheme()
        => new(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));

    private static AuthenticationFailedContext BuildFailedContext(HttpContext httpContext, Exception exception)
        => new(httpContext, BuildScheme(), new JwtBearerOptions()) { Exception = exception };

    private static JwtBearerChallengeContext BuildChallengeContext(HttpContext httpContext)
        => new(httpContext, BuildScheme(), new JwtBearerOptions(), new AuthenticationProperties());

    private static JsonDocument ReadProblem(MemoryStream body)
    {
        body.Position = 0;
        return JsonDocument.Parse(body.ToArray());
    }
}
