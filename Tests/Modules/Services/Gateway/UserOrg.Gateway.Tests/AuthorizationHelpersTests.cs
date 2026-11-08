using Forge.Core.Features.EndpointMap.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using UserOrg.Gateway.Configuration;
using UserOrg.Gateway.Features.Authorization;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

public sealed class AuthorizationHelpersTests
{
    // Project-scoped endpoint the caller is NOT a member of.
    private const string RequestedProject = "otherproj";

    private const string UserSub = "idp-user-42@einfra.cz";

    private const string UserActorId = "idp-user-42";

    private const string ServiceKeySub = "svc-key-7";

    private const string ServiceName = "svc-a";

    private const string AuditedPath = $"/api/v2/Project/{RequestedProject}/resources/42";

    private const string TruncatedAuditedPath = $"/api/v2/Project/{RequestedProject}/...";

    private const string ProjectPermission = "perm:project:remove";

    private const string DeniedAction = "gateway.authorization.denied";

    private const string BypassedAction = "gateway.authorization.bypassed";

    // AddAuditLogging<TSink> always activates TSink through DI (new ServiceDescriptor(..., typeof(TSink),
    // sinkLifetime)), so the fake instance the test inspects must be recovered by resolving
    // IAuditEventSink afterwards rather than by pre-registering one.
    private static IServiceProvider BuildAuditingServiceProvider(bool enforceProjectMembershipForSystemRoles = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IPermissionMatcher, PermissionMatcher>();
        builder.Services.AddSingleton(Options.Create(
            new GatewayAuthorizationOptions { EnforceProjectMembershipForSystemRoles = enforceProjectMembershipForSystemRoles }));
        builder.AddStepUpLogging(o => o.EnableOtlpExporter = false);
        builder.AddAuditLogging<RecordingAuditEventSink>(ServiceLifetime.Singleton);

        return builder.Services.BuildServiceProvider();
    }

    private static Task<(RecordingAuditEventSink Sink, HttpContext HttpContext, bool Authorized)> DenyRequestAsync(
        string path,
        ClaimsPrincipal user,
        Result<EndpointPermissionMatch?, Error> lookupResult)
        => AuthorizeAsync(user, EndpointServiceReturning(lookupResult), path: path, method: "GET");

    private static async Task<(RecordingAuditEventSink Sink, HttpContext HttpContext, bool Authorized)> AuthorizeAsync(
        ClaimsPrincipal user,
        IEndpointPermissionService? endpointService,
        UserPermissionsSnapshot? snapshot = null,
        string path = AuditedPath,
        string method = "DELETE",
        bool enforceProjectMembershipForSystemRoles = false,
        Exception? sinkFailure = null)
    {
        var serviceProvider = BuildAuditingServiceProvider(enforceProjectMembershipForSystemRoles);
        var sink = (RecordingAuditEventSink)serviceProvider.GetRequiredService<IAuditEventSink>();
        sink.ThrowOnWrite = sinkFailure;

        var cache = new Mock<IUserPermissionsCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(snapshot ?? new UserPermissionsSnapshot());

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Request.Path = path;
        httpContext.Request.Method = method;
        httpContext.Response.Body = new MemoryStream();
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

        var authorized = await AuthorizationHelpers.AuthorizeRequestAsync(
            httpContext, endpointService, user, cache.Object, serviceProvider);

        return (sink, httpContext, authorized);
    }

    private static IEndpointPermissionService EndpointServiceReturning(Result<EndpointPermissionMatch?, Error> lookupResult)
    {
        var endpointService = new Mock<IEndpointPermissionService>();
        endpointService
            .Setup(s => s.GetEndpointPermissionsByPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lookupResult);
        return endpointService.Object;
    }

    private static Result<EndpointPermissionMatch?, Error> Matched(params string[] permissionTemplates) =>
        Result<EndpointPermissionMatch?, Error>.Success(new EndpointPermissionMatch(
            "Test Endpoint",
            permissionTemplates,
            new Dictionary<string, string> { ["ProjectShortName"] = RequestedProject }));

    private static ClaimsPrincipal PrincipalWithSub(string? sub) =>
        new(new ClaimsIdentity(sub is null ? [] : new[] { new Claim(JwtRegisteredClaimNames.Sub, sub) }));

    private static ClaimsPrincipal ApiKeyPrincipal(EServiceType? serviceType)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, ServiceKeySub),
            new(JwtRegisteredClaimNames.PreferredUsername, ServiceName),
            new(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey)),
        };
        if (serviceType is not null)
        {
            claims.Add(new Claim(AuthConstants.ServiceTypeClaimType, serviceType.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    private static UserPermissionsSnapshot Snapshot(bool holdsPermission, bool isProjectMember) => new()
    {
        PermissionKeys = holdsPermission
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ProjectPermission }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        EffectiveRoleClaim = RoleInfoConstants.PlatformUserRoleName,
        UserProjectShortNames = isProjectMember
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RequestedProject }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    };

    private sealed record Refusal(
        ClaimsPrincipal User,
        Result<EndpointPermissionMatch?, Error> Lookup,
        UserPermissionsSnapshot Permissions,
        string Problem,
        string Reason,
        string ActorId,
        string ActorType,
        string[] DataKeys);

    private static readonly string[] BaseDataKeys = ["path", "method", "sub"];

    private static Refusal RefusalAt(string branch) => branch switch
    {
        "no_permission_mapping" => new(PrincipalWithSub(UserSub), Result<EndpointPermissionMatch?, Error>.Success(null), Snapshot(true, true),
            "auth.no_permission_mapping", "gateway.no_permission_mapping", UserActorId, "user", BaseDataKeys),
        "permission_lookup_failed" => new(PrincipalWithSub(UserSub),
            Result<EndpointPermissionMatch?, Error>.Failure(new ErrorBuilder().WithDescription("endpoint map unavailable").WithSeverity(ErrorSeverity.Error).Build()),
            Snapshot(true, true), "auth.no_permission_mapping", "gateway.permission_lookup_failed", UserActorId, "user", BaseDataKeys),
        "required_ddi_permission" => new(ApiKeyPrincipal(EServiceType.DDI), Matched($"perm:service:{ServiceName}"), Snapshot(true, true),
            "auth.required_ddi_permission", "gateway.required_ddi_permission", ServiceKeySub, "service", BaseDataKeys),
        "required_provider_permission" => new(ApiKeyPrincipal(EServiceType.Provider), Matched(ProjectPermission), Snapshot(true, true),
            "auth.required_provider_permission", "gateway.required_provider_permission", ServiceKeySub, "service", BaseDataKeys),
        "required_service_permission" => new(ApiKeyPrincipal(null), Matched("perm:service:another-service"), Snapshot(true, true),
            "auth.required_service_permission", "gateway.required_service_permission", ServiceKeySub, "service", BaseDataKeys),
        "insufficient_permissions" => new(PrincipalWithSub(UserSub), Matched(ProjectPermission), Snapshot(false, true),
            "auth.insufficient_permissions", "gateway.insufficient_permissions", UserActorId, "user", BaseDataKeys),
        "insufficient_project_permissions" => new(PrincipalWithSub(UserSub), Matched(ProjectPermission), Snapshot(true, false),
            "auth.insufficient_project_permissions", "gateway.insufficient_project_permissions", UserActorId, "user", [.. BaseDataKeys, "project"]),
        _ => throw new ArgumentOutOfRangeException(nameof(branch), branch, null),
    };

    private static string? ProblemCode(HttpContext httpContext)
    {
        using var problem = System.Text.Json.JsonDocument.Parse(((MemoryStream)httpContext.Response.Body).ToArray());
        return problem.RootElement.GetProperty("code").GetString();
    }

    [Theory]
    [InlineData("idp-user-42", "idp-user-42", "user")]
    // A realm-qualified sub is normalized to the bare identity Core records (Caller.ResolveIdpId).
    [InlineData("idp-user-42@einfra.cz", "idp-user-42", "user")]
    [InlineData(null, "anonymous", "anonymous")]
    public async Task Should_WriteDeniedAuditEvent_When_NoPermissionMapping(
        string? sub, string expectedActorId, string expectedActorType)
    {
        var (sink, httpContext, authorized) = await DenyRequestAsync(
            "/api/unmapped",
            PrincipalWithSub(sub),
            Result<EndpointPermissionMatch?, Error>.Success(null));

        Assert.False(authorized);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);

        var (auditEvent, responseStatusCodeAtWrite) = Assert.Single(sink.Writes);
        Assert.Equal(StatusCodes.Status200OK, responseStatusCodeAtWrite);
        Assert.Equal(GatewayAuditActions.AuthorizationDenied, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(expectedActorId, auditEvent.ActorId);
        Assert.Equal(expectedActorType, auditEvent.ActorType);
        // The cause is carried by the first-class Reason property at every audited site, Core's included.
        Assert.Equal("gateway.no_permission_mapping", auditEvent.Reason);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal(new HashSet<string> { "path", "method", "sub" }, auditEvent.Data.Keys.ToHashSet());
        Assert.Equal("/api/unmapped", auditEvent.Data["path"]);
        Assert.Equal("GET", auditEvent.Data["method"]);
        Assert.Equal(sub, auditEvent.Data["sub"]);
    }

    [Fact]
    public async Task Should_RecordServiceActorType_When_CallerAuthenticatedWithApiKey()
    {
        var serviceCaller = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, "waldur"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey))]));

        var (sink, _, _) = await DenyRequestAsync(
            "/api/unmapped", serviceCaller, Result<EndpointPermissionMatch?, Error>.Success(null));

        var (auditEvent, _) = Assert.Single(sink.Writes);
        Assert.Equal("service", auditEvent.ActorType);
    }

    [Fact]
    public async Task Should_RecordLookupFailureReason_When_PermissionLookupFails()
    {
        var lookupFailure = Result<EndpointPermissionMatch?, Error>.Failure(
            new ErrorBuilder().WithDescription("endpoint map unavailable").WithSeverity(ErrorSeverity.Error).Build());

        var (sink, httpContext, authorized) = await DenyRequestAsync(
            "/api/unmapped", PrincipalWithSub("idp-user-42"), lookupFailure);

        Assert.False(authorized);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);

        var (auditEvent, _) = Assert.Single(sink.Writes);
        Assert.Equal(GatewayAuditActions.AuthorizationDenied, auditEvent.Action);
        Assert.Equal("gateway.permission_lookup_failed", auditEvent.Reason);
    }

    [Fact]
    public async Task Should_RecordOnlyLeadingPathSegments_When_PathIsDeeperThanTheCap()
    {
        const string secretToken = "3f6a1c2e-secret-token";

        var (sink, _, _) = await DenyRequestAsync(
            $"/api/v2/Public/ResourceRequestActions/{secretToken}",
            PrincipalWithSub("idp-user-42"),
            Result<EndpointPermissionMatch?, Error>.Success(null));

        var (auditEvent, _) = Assert.Single(sink.Writes);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal("/api/v2/Public/ResourceRequestActions/...", auditEvent.Data["path"]);
    }

    [Theory]
    [InlineData("no_permission_mapping")]
    [InlineData("permission_lookup_failed")]
    [InlineData("required_ddi_permission")]
    [InlineData("required_provider_permission")]
    [InlineData("required_service_permission")]
    [InlineData("insufficient_permissions")]
    [InlineData("insufficient_project_permissions")]
    public async Task Should_WriteOneDeniedAuditEventBeforeThe403_When_AnyBranchRefusesTheCaller(string branch)
    {
        var refusal = RefusalAt(branch);

        var (sink, httpContext, authorized) = await AuthorizeAsync(
            refusal.User, EndpointServiceReturning(refusal.Lookup), refusal.Permissions);

        Assert.False(authorized);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.Equal(refusal.Problem, ProblemCode(httpContext));

        var (auditEvent, responseStatusCodeAtWrite) = Assert.Single(sink.Writes);
        Assert.Equal(StatusCodes.Status200OK, responseStatusCodeAtWrite);
        Assert.Equal(DeniedAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(refusal.Reason, auditEvent.Reason);
        Assert.Equal(refusal.ActorId, auditEvent.ActorId);
        Assert.Equal(refusal.ActorType, auditEvent.ActorType);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal(refusal.DataKeys.ToHashSet(), auditEvent.Data.Keys.ToHashSet());
        Assert.Equal(TruncatedAuditedPath, auditEvent.Data["path"]);
        Assert.Equal("DELETE", auditEvent.Data["method"]);
        Assert.Equal(refusal.User.FindFirstValue(JwtRegisteredClaimNames.Sub), auditEvent.Data["sub"]);
    }

    [Fact]
    public async Task Should_RecordTheRequestedProject_When_TheCallerIsNotAMemberOfIt()
    {
        var refusal = RefusalAt("insufficient_project_permissions");

        var (sink, _, _) = await AuthorizeAsync(refusal.User, EndpointServiceReturning(refusal.Lookup), refusal.Permissions);

        var (auditEvent, _) = Assert.Single(sink.Writes);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal(RequestedProject, auditEvent.Data["project"]);
    }

    [Theory]
    [InlineData("no_permission_mapping")]
    [InlineData("permission_lookup_failed")]
    [InlineData("required_ddi_permission")]
    [InlineData("required_provider_permission")]
    [InlineData("required_service_permission")]
    [InlineData("insufficient_permissions")]
    [InlineData("insufficient_project_permissions")]
    public async Task Should_PropagateTheSinkException_When_ADenialCannotBeAudited(string branch)
    {
        var refusal = RefusalAt(branch);
        var sinkFailure = new InvalidOperationException("audit sink unavailable");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthorizeAsync(
            refusal.User, EndpointServiceReturning(refusal.Lookup), refusal.Permissions, sinkFailure: sinkFailure));

        Assert.Same(sinkFailure, thrown);
    }

    [Theory]
    [InlineData(UserSub, false, UserActorId, "user")]
    [InlineData(ServiceKeySub, true, ServiceKeySub, "service")]
    [InlineData(null, false, "anonymous", "anonymous")]
    public async Task Should_WriteBypassedAuditEventBeforeAuthorizing_When_NoPermissionServiceIsRegistered(
        string? sub, bool apiKey, string expectedActorId, string expectedActorType)
    {
        var user = apiKey ? ApiKeyPrincipal(null) : PrincipalWithSub(sub);

        var (sink, httpContext, authorized) = await AuthorizeAsync(user, endpointService: null);

        Assert.True(authorized);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);

        var (auditEvent, responseStatusCodeAtWrite) = Assert.Single(sink.Writes);
        Assert.Equal(StatusCodes.Status200OK, responseStatusCodeAtWrite);
        Assert.Equal(BypassedAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("gateway.permission_service_missing", auditEvent.Reason);
        Assert.Equal(expectedActorId, auditEvent.ActorId);
        Assert.Equal(expectedActorType, auditEvent.ActorType);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal(new HashSet<string> { "path", "method", "sub" }, auditEvent.Data.Keys.ToHashSet());
        Assert.Equal(TruncatedAuditedPath, auditEvent.Data["path"]);
        Assert.Equal("DELETE", auditEvent.Data["method"]);
        Assert.Equal(sub, auditEvent.Data["sub"]);
    }

    [Fact]
    public async Task Should_PropagateTheSinkException_When_TheBypassCannotBeAudited()
    {
        var sinkFailure = new InvalidOperationException("audit sink unavailable");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthorizeAsync(
            PrincipalWithSub(UserSub), endpointService: null, sinkFailure: sinkFailure));

        Assert.Same(sinkFailure, thrown);
    }

    [Theory]
    [InlineData("empty_permission_templates")]
    [InlineData("ddi_service_permitted")]
    [InlineData("provider_service_permitted")]
    [InlineData("named_service_permitted")]
    [InlineData("user_permitted")]
    public async Task Should_WriteNoAuditEvent_When_TheRequestIsAuthorizedNormally(string allowPath)
    {
        var (user, lookup) = allowPath switch
        {
            "empty_permission_templates" => (PrincipalWithSub(UserSub), Matched()),
            "ddi_service_permitted" => (ApiKeyPrincipal(EServiceType.DDI), Matched("perm:servicetype:ddi")),
            "provider_service_permitted" => (ApiKeyPrincipal(EServiceType.Provider), Matched("perm:provider:write")),
            "named_service_permitted" => (ApiKeyPrincipal(null), Matched($"perm:service:{ServiceName}")),
            "user_permitted" => (PrincipalWithSub(UserSub), Matched(ProjectPermission)),
            _ => throw new ArgumentOutOfRangeException(nameof(allowPath), allowPath, null),
        };

        var (sink, httpContext, authorized) = await AuthorizeAsync(
            user, EndpointServiceReturning(lookup), Snapshot(holdsPermission: true, isProjectMember: true));

        Assert.True(authorized);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Empty(sink.Writes);
    }

    [Theory]
    // System role, no configuration → bypass membership check (default behaviour).
    [InlineData(RoleInfoConstants.PlatformAdministratorRoleName, false, true)]
    [InlineData(RoleInfoConstants.ProviderAdministratorRoleName, false, true)]
    // System role, enforcement switched on → membership enforced → forbidden.
    [InlineData(RoleInfoConstants.PlatformAdministratorRoleName, true, false)]
    // Non-system role → membership always enforced → forbidden.
    [InlineData(RoleInfoConstants.PlatformUserRoleName, false, false)]
    [InlineData(RoleInfoConstants.PlatformUserRoleName, true, false)]
    public async Task ProjectMembership_BypassedForSystemRoles_UnlessEnforced(
        string effectiveRoleClaim,
        bool enforceForSystemRoles,
        bool expectedAuthorized)
    {
        var match = new EndpointPermissionMatch(
            "Remove Project",
            new[] { "perm:project:remove" },
            new Dictionary<string, string> { ["ProjectShortName"] = RequestedProject });

        var snapshot = new UserPermissionsSnapshot
        {
            PermissionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "perm:project:remove" },
            EffectiveRoleClaim = effectiveRoleClaim,
            UserProjectShortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) // not a member of any project
        };

        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var (sink, httpContext, authorized) = await AuthorizeAsync(
            user,
            EndpointServiceReturning(Result<EndpointPermissionMatch?, Error>.Success(match)),
            snapshot,
            path: $"/api/Project/{RequestedProject}",
            method: "DELETE",
            enforceProjectMembershipForSystemRoles: enforceForSystemRoles);

        Assert.Equal(expectedAuthorized, authorized);
        Assert.Equal(expectedAuthorized ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);

        if (expectedAuthorized)
        {
            Assert.Empty(sink.Writes);
        }
        else
        {
            var (auditEvent, _) = Assert.Single(sink.Writes);
            Assert.Equal(DeniedAction, auditEvent.Action);
            Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
            Assert.Equal("gateway.insufficient_project_permissions", auditEvent.Reason);
        }
    }
}
