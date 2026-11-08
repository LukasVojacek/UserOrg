using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

using Forge.Core.Features.EndpointMap.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using UserOrg.Gateway.Features.Authorization;
using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Features.RouteExistence;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The defect of T-187: a path no API route serves is answered by the gateway's own authentication and
/// authorization stage (401 without a token, 403 <c>auth.no_permission_mapping</c> with one) instead of
/// 404. Both faces are driven through the catch-all forwarder's real transform chain.
/// </summary>
public sealed class UnmatchedRouteNotFoundTests
{
    private const string UnservedPath = RouteSurfaceFixture.UnservedV2Path;

    private sealed class NullAuditEventSink : IAuditEventSink
    {
        public ValueTask<AuditWriteResult> WriteAsync(AuditEvent auditEvent)
            => ValueTask.FromResult(AuditWriteResult.Stored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_Answer404_When_NoRouteServesThePath(bool authenticated)
    {
        var (httpContext, body) = await SendAsync(
            RouteSurfaceFixture.ProviderOf(RouteSurfaceFixture.FullSurface()), UnservedPath, authenticated);

        using (body)
        {
            Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
            Assert.Equal(ProblemResponse.ContentType, httpContext.Response.ContentType);

            using var problem = JsonDocument.Parse(body.ToArray());
            Assert.Equal(RouteExistenceTransformExtensions.NotFoundCode, problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(StatusCodes.Status404NotFound, problem.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Not found", problem.RootElement.GetProperty("title").GetString());
            Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("requestId").GetString()));
        }
    }

    /// <summary>
    /// The regression that separates this fix from the wrong one: a path a document does describe, but the
    /// RBAC map has no entry for, keeps the loud 403 that made the ADR-0059 gaps findable.
    /// </summary>
    [Fact]
    public async Task Should_Keep403NoPermissionMapping_When_TheRouteExistsButTheRbacMapHasNoEntry()
    {
        var (httpContext, body) = await SendAsync(
            RouteSurfaceFixture.ProviderOf(RouteSurfaceFixture.FullSurface()),
            RouteSurfaceFixture.ServedV2Path,
            authenticated: true);

        using (body)
        {
            AssertProblem(httpContext, body, StatusCodes.Status403Forbidden, "auth.no_permission_mapping");
        }
    }

    /// <summary>
    /// The degraded oracle: nothing loaded, so the request carries on into the auth stage and is answered
    /// exactly as it is today. An empty surface must never report the API as non-existent.
    /// </summary>
    [Theory]
    [InlineData(false, StatusCodes.Status401Unauthorized, "auth.unauthorized")]
    [InlineData(true, StatusCodes.Status403Forbidden, "auth.no_permission_mapping")]
    public async Task Should_KeepTodaysAnswer_When_TheOracleHasLoadedNothing(
        bool authenticated, int expectedStatus, string expectedCode)
    {
        var (httpContext, body) = await SendAsync(
            RouteSurfaceFixture.DegradedProvider(), UnservedPath, authenticated);

        using (body)
        {
            AssertProblem(httpContext, body, expectedStatus, expectedCode);
        }
    }

    private static void AssertProblem(HttpContext httpContext, MemoryStream body, int expectedStatus, string expectedCode)
    {
        Assert.Equal(expectedStatus, httpContext.Response.StatusCode);

        using var problem = JsonDocument.Parse(body.ToArray());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<(HttpContext HttpContext, MemoryStream Body)> SendAsync(
        RouteSurfaceProvider routeSurfaceProvider, string path, bool authenticated)
    {
        using var services = BuildServices(routeSurfaceProvider);
        var body = new MemoryStream();
        var httpContext = CatchAllForwarderTransformHarness.BuildHttpContext(
            path,
            authenticated ? BearerUser() : CatchAllForwarderTransformHarness.AnonymousUser(),
            services,
            body);

        if (authenticated)
        {
            httpContext.Request.Headers.Authorization = "Bearer irrelevant";
        }

        await CatchAllForwarderTransformHarness.ApplyCatchAllTransformsAsync(httpContext);
        return (httpContext, body);
    }

    private static ClaimsPrincipal BearerUser() =>
        new(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "idp-user-42"),
                new Claim(JwtRegisteredClaimNames.PreferredUsername, "user-42"),
                new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken))
            ],
            "TestAuthType"));

    /// <summary>
    /// The gateway pipeline's collaborators, with the endpoint permission map answering "no mapping" for
    /// every path - the state that produces today's 403 and the one the fix must not disturb for a real
    /// route (ADR-0059).
    /// </summary>
    private static ServiceProvider BuildServices(RouteSurfaceProvider routeSurfaceProvider)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHttpContextAccessor();
        builder.AddStepUpLogging(o => o.EnableOtlpExporter = false);
        builder.AddAuditLogging<NullAuditEventSink>(ServiceLifetime.Singleton);

        var endpointPermissionService = new Mock<IEndpointPermissionService>();
        endpointPermissionService
            .Setup(s => s.GetEndpointPermissionsByPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EndpointPermissionMatch?, Error>.Success(null));

        builder.Services.AddSingleton(endpointPermissionService.Object);
        builder.Services.AddSingleton(Mock.Of<IInternalTokenProvider>());
        builder.Services.AddSingleton(Mock.Of<IUserPermissionsCache>());
        builder.Services.AddSingleton(routeSurfaceProvider);

        return builder.Services.BuildServiceProvider();
    }
}