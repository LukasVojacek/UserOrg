using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Shared;
using UserOrg.Shared.FeatureManagement;

namespace UserOrganizationManagement.Application.Tests.TrustedEdge;

internal sealed class TrustedEdgeBootFactory : WebApplicationFactory<Program>
{
    public const string Subject = "3f6f1c0e-9a51-4f4e-8c1d-2b7de5a0c001";
    public const string EmailAddress = "trusted.edge@example.org";
    public const string PreferredUsername = "trusted.edge";
    public const string Role = RoleInfoConstants.PlatformUserRoleName;

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("trusted-edge-boot-test-signing-key-0123456789abcdef"));

    static TrustedEdgeBootFactory()
    {
        ProgramBootEnvironment.Apply();
    }

    public RecordingAuditEventSink AuditSink { get; } = new();

    public ConcurrentQueue<ClaimsPrincipal> ValidatedPrincipals { get; } = new();

    public static string MintGatewayToken() =>
        GatewayTestJwt.Mint(SigningKey, ProgramBootEnvironment.DefaultGatewayAuthority, new()
        {
            [JwtRegisteredClaimNames.Sub] = Subject,
            [JwtRegisteredClaimNames.PreferredUsername] = PreferredUsername,
            [ClaimTypes.Role] = Role,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Email] = EmailAddress,
        });

    /// <summary>
    /// Mints a GatewayJwt token the way the gateway's <c>TokenService</c> does, see
    /// <see cref="GatewayTestJwt.MintAsTheTokenServiceWritesIt"/>.
    /// </summary>
    public static string MintGatewayTokenAsTheTokenServiceWritesIt(string role) =>
        GatewayTestJwt.MintAsTheTokenServiceWritesIt(SigningKey, ProgramBootEnvironment.DefaultGatewayAuthority, new()
        {
            [JwtRegisteredClaimNames.Sub] = Subject,
            [JwtRegisteredClaimNames.PreferredUsername] = PreferredUsername,
            [ClaimTypes.Role] = role,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iat] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            [EAuthorizationMethod.ClaimType] = nameof(EAuthorizationMethod.JwtBearerToken),
            [JwtRegisteredClaimNames.Email] = EmailAddress,
        });

    public Task<HttpContext> SendAsync(
        string method,
        string path,
        IPAddress peer,
        IReadOnlyDictionary<string, string> headers,
        bool authenticated = false,
        string? token = null) =>
        Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = peer;
            context.Request.Method = method;
            context.Request.Path = path;
            foreach (var (name, value) in headers)
            {
                context.Request.Headers[name] = value;
            }

            var bearer = token ?? (authenticated ? MintGatewayToken() : null);
            if (bearer is not null)
            {
                context.Request.Headers.Authorization = $"Bearer {bearer}";
            }
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"FeatureManagement:{FeatureFlags.V2Api}", "true");
        builder.UseSetting("FeatureManagement:ReadOnlyMode", "true");
        builder.UseSetting("IpRateLimiting:EnableEndpointRateLimiting", "true");
        builder.UseSetting("IpRateLimiting:StackBlockedRequests", "false");
        builder.UseSetting("IpRateLimiting:RealIpHeader", "X-Real-IP");
        builder.UseSetting("IpRateLimiting:ClientIdHeader", "X-ClientId");
        builder.UseSetting("IpRateLimiting:HttpStatusCode", "429");
        builder.UseSetting("IpRateLimiting:EndpointWhitelist:0", "get:/api/*");
        builder.UseSetting("IpRateLimiting:ClientWhitelist:0", "127.0.0.1");
        builder.UseSetting("IpRateLimiting:GeneralRules:0:Endpoint", "*");
        builder.UseSetting("IpRateLimiting:GeneralRules:0:Period", "1h");
        builder.UseSetting("IpRateLimiting:GeneralRules:0:Limit", "1");

        builder.ConfigureTestServices(services =>
        {
            ProgramBootEnvironment.RemoveHostedServices(services);

            services.RemoveAll<IAuditEventSink>();
            services.AddSingleton<IAuditEventSink>(AuditSink);

            GatewayTestJwt.Accept(services, SigningKey, ProgramBootEnvironment.DefaultGatewayAuthority, options =>
            {
                var validated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    await validated(context);
                    if (context.Principal is not null)
                    {
                        ValidatedPrincipals.Enqueue(context.Principal);
                    }
                };
            });
        });
    }
}