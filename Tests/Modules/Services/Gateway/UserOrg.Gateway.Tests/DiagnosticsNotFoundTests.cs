using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.RouteExistence;
using UserOrg.Gateway.Startup;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The defect of T-192: the diagnostics short circuit at the top of the catch-all forwarder chain answered
/// <c>/health</c> and <c>/alive</c> with a bare 404 and an empty body, while every other gateway-authored
/// 4xx on the proxied routes goes through <see cref="ProblemResponse"/> (ADR-0106 consequences).
/// </summary>
public sealed class DiagnosticsNotFoundTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/alive")]
    public async Task Should_AnswerProblemJson_When_ADiagnosticsPathReachesTheCatchAll(string path)
    {
        var (httpContext, body) = await SendAsync(path);

        using (body)
        {
            Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
            Assert.Equal(ProblemResponse.ContentType, httpContext.Response.ContentType);

            using var problem = JsonDocument.Parse(body.ToArray());
            Assert.Equal(
                UserOrgForwardingExtensions.DiagnosticsNotFoundCode,
                problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(StatusCodes.Status404NotFound, problem.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(path, problem.RootElement.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("requestId").GetString()));
        }
    }

    /// <summary>
    /// The diagnostics code says something the route-existence code cannot: the oracle answers
    /// <see cref="ERouteExistence.Unknown"/> outside <c>/api</c>, so it never judged these paths at all.
    /// </summary>
    [Fact]
    public void Should_NotReuseTheRouteExistenceCode_When_NamingTheDiagnosticsAnswer()
    {
        Assert.NotEqual(
            RouteExistenceTransformExtensions.NotFoundCode,
            UserOrgForwardingExtensions.DiagnosticsNotFoundCode);

        Assert.Equal(
            ERouteExistence.Unknown,
            RouteSurfaceFixture.FullSurface().Evaluate("/health"));
    }

    private static async Task<(HttpContext HttpContext, MemoryStream Body)> SendAsync(string path)
    {
        await using var services = new ServiceCollection()
            .AddSingleton(RouteSurfaceFixture.ProviderOf(RouteSurfaceFixture.FullSurface()))
            .BuildServiceProvider();

        var body = new MemoryStream();
        var httpContext = CatchAllForwarderTransformHarness.BuildHttpContext(
            path, CatchAllForwarderTransformHarness.AnonymousUser(), services, body);

        await CatchAllForwarderTransformHarness.ApplyCatchAllTransformsAsync(httpContext);
        return (httpContext, body);
    }
}