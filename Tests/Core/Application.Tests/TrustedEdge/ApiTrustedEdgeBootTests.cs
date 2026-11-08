using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

using AspNetCoreRateLimit;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Shared;

namespace UserOrganizationManagement.Application.Tests.TrustedEdge;

[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiTrustedEdgeBootTests
{
    private static readonly TrustedEdgeBootFactory _factory = new();

    private static readonly IPAddress Gateway = IPAddress.Loopback;

    [Fact]
    public async Task Should_RecordTheForwardedClientAndTheTokenSub_When_AReadOnlyBlockedRequestArrivesThroughTheGateway()
    {
        const string client = "203.0.113.77";
        var before = _factory.AuditSink.Writes.Count;

        var context = await _factory.SendAsync(
            HttpMethods.Post,
            "/api/v2/Projects",
            Gateway,
            new Dictionary<string, string> { ["X-Forwarded-For"] = client },
            authenticated: true);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var blocked = Assert.Single(_factory.AuditSink.Writes.Skip(before));
        Assert.Equal(client, blocked.SourceIp);
        Assert.Equal(TrustedEdgeBootFactory.Subject, blocked.ActorId);
    }

    [Fact]
    public async Task Should_BindRawSubRoleAndEmail_When_TheGatewayJwtCarriesTheClaimsTheTokenServiceEmits()
    {
        _ = await _factory.SendAsync(
            HttpMethods.Post,
            "/api/v2/Projects",
            Gateway,
            new Dictionary<string, string>(),
            authenticated: true);

        var principal = _factory.ValidatedPrincipals.Last();
        var caller = await BindCallerAsync(principal);

        Assert.Equal(TrustedEdgeBootFactory.Subject, Member(caller, "RawSub"));
        Assert.Equal(TrustedEdgeBootFactory.Role, Member(caller, "Role"));
        Assert.Equal(TrustedEdgeBootFactory.EmailAddress, Member(Member(caller, "Email"), "Value"));
        Assert.Null(principal.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Null(principal.FindFirst(ClaimTypes.Email));
    }

    [Fact]
    public async Task Should_TreatTheCallerAsAdmin_When_TheGatewayJwtCarriesTheRoleUnderTheNameTheTokenServiceWrites()
    {
        var token = TrustedEdgeBootFactory.MintGatewayTokenAsTheTokenServiceWritesIt(RoleInfoConstants.PlatformAdministratorRoleName);
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(token.Split('.')[1]));
        Assert.Equal(RoleInfoConstants.PlatformAdministratorRoleName, payload.RootElement.GetProperty("role").GetString());

        _ = await _factory.SendAsync(
            HttpMethods.Post,
            "/api/v2/Projects",
            Gateway,
            new Dictionary<string, string>(),
            token: token);

        var principal = _factory.ValidatedPrincipals.Last();
        var caller = await BindCallerAsync(principal);
        var context = new DefaultHttpContext { User = principal, RequestServices = _factory.Services };

        Assert.Equal(RoleInfoConstants.PlatformAdministratorRoleName, Member(caller, "Role"));
        Assert.Equal(TrustedEdgeBootFactory.Subject, Member(caller, "RawSub"));
        Assert.True(global::UserOrg.Shared.AuthenticationExtensions.GetCallerInfo(context).IsPlatformAdmin);
        Assert.True(UserOrganizationManagement.API.Authentication.AuthenticationExtensions.TryGetIsAdmin(context, out var isAdmin) && isAdmin);
        Assert.True(UserOrganizationManagement.API.Authentication.AuthenticationExtensions.TryGetOpenIdUserInfoModel(context, out var openId));
        Assert.Equal(RoleInfoConstants.PlatformAdministratorRoleName, openId.Role);
        Assert.Null(principal.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Null(principal.FindFirst(ClaimTypes.Email));
    }

    [Fact]
    public void Should_ResolveTheRateLimiterClientHeadersToEmpty_When_TheBoundConfigurationStillNamesThem()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IpRateLimitOptions>>().Value;

        Assert.Equal(string.Empty, options.RealIpHeader);
        Assert.Equal(string.Empty, options.ClientIdHeader);
        Assert.Empty(options.ClientWhitelist ?? []);
    }

    [Fact]
    public async Task Should_CountTheRequestAgainstTheClientIp_When_AClientSendsAWhitelistedClientId()
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = "203.0.113.21",
            ["X-ClientId"] = "127.0.0.1",
        };

        var first = await _factory.SendAsync(HttpMethods.Post, "/api/rate-limit-probe/client-id", Gateway, headers);
        var second = await _factory.SendAsync(HttpMethods.Post, "/api/rate-limit-probe/client-id", Gateway, headers);

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, first.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
    }

    [Fact]
    public async Task Should_IgnoreAClientSetRealIp_When_TheSameForwardedClientRotatesIt()
    {
        var first = await _factory.SendAsync(HttpMethods.Post, "/api/rate-limit-probe/rotating-real-ip", Gateway, new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = "203.0.113.31",
            ["X-Real-IP"] = "198.51.100.1",
        });
        var second = await _factory.SendAsync(HttpMethods.Post, "/api/rate-limit-probe/rotating-real-ip", Gateway, new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = "203.0.113.31",
            ["X-Real-IP"] = "198.51.100.2",
        });

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, first.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
    }

    [Fact]
    public async Task Should_KeyOnTheForwardedClientAddress_When_TwoClientsShareAClientSetRealIp()
    {
        var first = await _factory.SendAsync(HttpMethods.Post, "/api/rate-limit-probe/shared-real-ip", Gateway, new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = "203.0.113.41",
            ["X-Real-IP"] = "198.51.100.9",
        });
        var second = await _factory.SendAsync(HttpMethods.Post, "/api/rate-limit-probe/shared-real-ip", Gateway, new Dictionary<string, string>
        {
            ["X-Forwarded-For"] = "203.0.113.42",
            ["X-Real-IP"] = "198.51.100.9",
        });

        Assert.NotEqual(StatusCodes.Status429TooManyRequests, first.Response.StatusCode);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
    }

    private static async Task<object> BindCallerAsync(ClaimsPrincipal principal)
    {
        var callerType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Core.Shared.Auth.Caller"))
            .Single(type => type is not null)!;
        var bind = callerType.GetMethod("BindAsync", BindingFlags.Public | BindingFlags.Static, [typeof(HttpContext)])!;

        var pending = bind.Invoke(null, [new DefaultHttpContext { User = principal }])!;
        var task = (Task)pending.GetType().GetMethod("AsTask")!.Invoke(pending, null)!;
        await task;

        var caller = task.GetType().GetProperty("Result")!.GetValue(task);
        Assert.True(caller is not null, "Caller.BindAsync returned null for the validated GatewayJwt principal");
        return caller;
    }

    private static object? Member(object? instance, string name)
    {
        Assert.True(instance is not null, $"cannot read {name} from null");
        return instance.GetType().GetProperty(name)!.GetValue(instance);
    }
}