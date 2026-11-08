using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Authentication;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// <c>OnTokenValidated</c> rejects with <c>context.Fail</c>, which never raises <c>OnAuthenticationFailed</c>,
/// so it must store its own <see cref="AuthenticationFailure"/> for the forwarder / challenge to render (#293):
/// a revoked token is a 401 the Portal may refresh, a token without <c>sub</c> is a 422 it must not retry.
/// </summary>
public sealed class ServicesExtensionsTokenValidatedFailureTests
{
    private const string Authority = "https://idp.test";

    [Fact]
    public async Task Should_StoreTokenRevoked401_When_IntrospectionReportsInactive()
    {
        var (context, _) = BuildContext(BuildPrincipal(sub: "sub-1"), introspectionActive: false);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        var failure = Assert.IsType<AuthenticationFailure>(context.HttpContext.Items[ProblemResponse.AuthenticationFailureItemKey]);
        Assert.Equal(StatusCodes.Status401Unauthorized, failure.Status);
        Assert.Equal("auth.token_revoked", failure.Code);
    }

    [Fact]
    public async Task Should_RenderTokenRevokedProblem_When_ForwarderWritesAfterInactiveIntrospection()
    {
        var (context, body) = BuildContext(BuildPrincipal(sub: "sub-1"), introspectionActive: false);
        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        await ProblemResponse.WriteAuthenticationFailureAsync(context.HttpContext);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.HttpContext.Response.StatusCode);
        Assert.Equal(ProblemResponse.ContentType, context.HttpContext.Response.ContentType);
        Assert.Equal("Bearer error=\"invalid_token\"", context.HttpContext.Response.Headers.WWWAuthenticate.ToString());
        using var problem = JsonDocument.Parse(body.ToArray());
        Assert.Equal("auth.token_revoked", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Should_StoreMissingClaims422_When_TokenHasNoSub()
    {
        var (context, _) = BuildContext(BuildPrincipal(sub: null), introspectionActive: true);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        var failure = Assert.IsType<AuthenticationFailure>(context.HttpContext.Items[ProblemResponse.AuthenticationFailureItemKey]);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, failure.Status);
        Assert.Equal("auth.token_missing_claims", failure.Code);
    }

    [Fact]
    public async Task Should_StoreNothing_When_IntrospectionReportsActive()
    {
        var (context, _) = BuildContext(BuildPrincipal(sub: "sub-1"), introspectionActive: true);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Null(context.Result);
        Assert.False(context.HttpContext.Items.ContainsKey(ProblemResponse.AuthenticationFailureItemKey));
    }

    [Fact]
    public async Task Should_AddTheIntrospectedRoles_When_IntrospectionReturnsRoles()
    {
        var introspection = new StubIntrospectionService(true, new Dictionary<string, string[]> { ["role"] = ["role-a", "role-b"] });
        var (context, _) = BuildContext(BuildPrincipal(sub: "sub-1"), introspection);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Null(context.Result);
        var roles = context.Principal!.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList();
        Assert.Contains("role-a", roles);
        Assert.Contains("role-b", roles);
    }

    [Fact]
    public async Task Should_TakeThePreferredUsernameFromIntrospection_When_TheTokenCarriesAnEmptyOne()
    {
        var introspection = new StubIntrospectionService(true, new Dictionary<string, string[]> { ["preferred_username"] = ["introspected-user"] });
        var (context, _) = BuildContext(BuildPrincipal(sub: "sub-1", preferredUsername: string.Empty), introspection);

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.Null(context.Result);
        var usernames = context.Principal!.FindAll(JwtRegisteredClaimNames.PreferredUsername).Select(claim => claim.Value);
        Assert.Contains("introspected-user", usernames);
    }

    [Fact]
    public async Task Should_FailClosedWithoutStampingTheGatewayIdentity_When_IntrospectionThrows()
    {
        var (context, _) = BuildContext(BuildPrincipal(sub: "sub-1"), new ThrowingIntrospectionService());

        await ServicesExtensions.CreateJwtBearerEvents(Authority).OnTokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.Null(context.Principal!.FindFirst(EAuthorizationMethod.ClaimType));
    }

    private static (TokenValidatedContext Context, MemoryStream Body) BuildContext(ClaimsPrincipal principal, bool introspectionActive)
        => BuildContext(principal, new StubIntrospectionService(introspectionActive, new Dictionary<string, string[]>()));

    private static (TokenValidatedContext Context, MemoryStream Body) BuildContext(ClaimsPrincipal principal, ITokenIntrospectionService introspection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(introspection);
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Request.Path = "/api/User/Preferences";
        httpContext.Response.Body = body;
        httpContext.Items[ServicesExtensions.BearerTokenItemKey] = "Bearer opaque-token";
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));
        return (new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions()) { Principal = principal }, body);
    }

    private static ClaimsPrincipal BuildPrincipal(string? sub, string preferredUsername = "user-1")
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.PreferredUsername, preferredUsername) };
        if (sub is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, sub));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    private sealed class StubIntrospectionService(bool active, IReadOnlyDictionary<string, string[]> claims) : ITokenIntrospectionService
    {
        public Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult(new IntrospectionResult(active, null, claims));
    }

    private sealed class ThrowingIntrospectionService : ITokenIntrospectionService
    {
        public Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("introspection endpoint unreachable");
    }
}
