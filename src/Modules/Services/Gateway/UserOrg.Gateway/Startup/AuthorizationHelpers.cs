using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Forge.Core.Features.EndpointMap.Services;

using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Options;

using UserOrg.Gateway.Configuration;
using UserOrg.Gateway.Features.Authorization;
using UserOrg.Shared;

namespace UserOrg.Gateway.Startup;

internal static class AuthorizationHelpers
{
    // AuthorizationHelpers is static and cannot be used as a generic type argument, so this
    // otherwise-empty type stands in as the IAuditLogger<T> category.
    private sealed class AuditCategory;

    private const string AnonymousActorId = "anonymous";

    // Core sets an actor type on every record it writes ("user", "action-link"); the gateway must too,
    // or its denials are the only ones where the kind of caller is unknowable. All the gateway knows at
    // denial time is which credential the request arrived with, so that is what these say.
    private const string UserActorType = "user";

    private const string ServiceActorType = "service";

    private const string AnonymousActorType = "anonymous";

    // The gateway denies before any route is matched, so an unmapped path can carry anything the
    // caller typed - including segments that are secrets (Public/ResourceRequestActions/{token}).
    // Recording only the leading segments keeps the route family, which is what a missing-mapping
    // investigation needs, and drops the tail identifiers where such secrets live.
    private const int MaxAuditedPathSegments = 4;

    private const string PathTruncationMarker = "/...";

    private const string RequiredDdiPermission = "perm:servicetype:ddi";

    private const string ProviderPermissionPrefix = "perm:provider:";

    private const string ProjectShortNameRouteKey = "projectshortname";

    public static async Task<bool> AuthorizeRequestAsync(
        HttpContext httpContext,
        Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService? endpointPermissionService,
        ClaimsPrincipal user,
        IUserPermissionsCache permissionsCache,
        IServiceProvider serviceProvider)
    {
        var request = DescribeRequest(httpContext, user, serviceProvider);

        if (endpointPermissionService is null)
        {
            await AuditBypassAsync(serviceProvider, user, request.Path, request.Method);
            return true;
        }

        var matchResult = await endpointPermissionService.GetEndpointPermissionsByPathAsync(
            request.Path,
            request.Method,
            httpContext.RequestAborted);

        if (!matchResult.IsSuccess || matchResult.Value is null)
        {
            // No route values are in scope here - the match failed to resolve, which is precisely why
            // we are denying. The raw sub travels alongside the normalized actor id because the
            // normalization is lossy and an audit record must not throw information away.
            return await DenyAsync(request,
                UnmappedDenialReason(matchResult.IsSuccess),
                "auth.no_permission_mapping",
                "No permission mapping",
                "No permission mapping exists for this endpoint");
        }

        var match = matchResult.Value;
        StashRouteTemplate(httpContext, match.PathTemplate);

        if (IsApiKeyCaller(user) && await AuthorizeServiceCallerAsync(request, match) is { } serviceCallerVerdict)
        {
            return serviceCallerVerdict;
        }

        if (match.PermissionTemplates.Count == 0)
        {
            return true;
        }

        return await AuthorizeUserAsync(request, match, permissionsCache);
    }

    private static AuthorizationRequest DescribeRequest(HttpContext httpContext, ClaimsPrincipal user, IServiceProvider serviceProvider) =>
        new(httpContext,
            user,
            serviceProvider,
            (httpContext.Request.PathBase + httpContext.Request.Path).Value ?? string.Empty,
            httpContext.Request.Method ?? string.Empty);

    private static string UnmappedDenialReason(bool lookupSucceeded) => lookupSucceeded
        ? GatewayAuditActions.DenialReasons.NoPermissionMapping
        : GatewayAuditActions.DenialReasons.PermissionLookupFailed;

    // Stashed for GatewayTracing, which renames the span at the end of the request. The lookup that
    // produced it is one this path already does for every proxied request, so naming the span costs
    // nothing extra.
    private static void StashRouteTemplate(HttpContext httpContext, string? pathTemplate)
    {
        if (!string.IsNullOrEmpty(pathTemplate))
        {
            httpContext.Items[GatewayTracing.RouteTemplateItemKey] = pathTemplate;
        }
    }

    // Null when the API-key caller carries no service identity the gateway can check; the request then
    // falls through to the user permission check.
    private static async Task<bool?> AuthorizeServiceCallerAsync(AuthorizationRequest request, EndpointPermissionMatch match)
    {
        var serviceType = request.User.FindFirstValue(AuthConstants.ServiceTypeClaimType);
        if (serviceType == EServiceType.DDI.ToString())
        {
            return await RequireDdiPermissionAsync(request, match);
        }

        // Provider service-key identity: a generic provider-write route cannot enumerate a
        // per-key perm:service:{name}, so pass any endpoint gated on a provider permission and let
        // Core perform the precise (claim-scoped) authorization (ADR-0011).
        if (serviceType == EServiceType.Provider.ToString())
        {
            return await RequireProviderPermissionAsync(request, match);
        }

        var serviceName = request.User.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername);
        return string.IsNullOrEmpty(serviceName) ? null : await RequireServicePermissionAsync(request, match, serviceName);
    }

    private static async Task<bool> RequireDdiPermissionAsync(AuthorizationRequest request, EndpointPermissionMatch match) =>
        HasPermissionTemplate(match, RequiredDdiPermission)
        || await DenyAsync(request,
            GatewayAuditActions.DenialReasons.RequiredDdiPermission,
            "auth.required_ddi_permission",
            "DDI service permission required",
            $"Service access requires permission '{RequiredDdiPermission}' for this endpoint");

    private static async Task<bool> RequireProviderPermissionAsync(AuthorizationRequest request, EndpointPermissionMatch match) =>
        match.PermissionTemplates.Any(p => p.StartsWith(ProviderPermissionPrefix, StringComparison.OrdinalIgnoreCase))
        || await DenyAsync(request,
            GatewayAuditActions.DenialReasons.RequiredProviderPermission,
            "auth.required_provider_permission",
            "Provider permission required",
            "Provider access requires a provider permission for this endpoint");

    private static async Task<bool> RequireServicePermissionAsync(AuthorizationRequest request, EndpointPermissionMatch match, string serviceName)
    {
        var requiredServicePerm = $"perm:service:{serviceName}";
        return HasPermissionTemplate(match, requiredServicePerm)
            || await DenyAsync(request,
                GatewayAuditActions.DenialReasons.RequiredServicePermission,
                "auth.required_service_permission",
                "Service permission required",
                $"Service access requires permission '{requiredServicePerm}' for this endpoint");
    }

    private static bool HasPermissionTemplate(EndpointPermissionMatch match, string permission) =>
        match.PermissionTemplates.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> AuthorizeUserAsync(AuthorizationRequest request, EndpointPermissionMatch match, IUserPermissionsCache permissionsCache)
    {
        // Retrieve user permissions from cache (single DB call per 30 s window)
        var idpUserId = request.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? string.Empty;
        var snapshot = await permissionsCache.GetAsync(idpUserId, request.HttpContext.RequestAborted);

        var permissionMatcher = request.Services.GetRequiredService<IPermissionMatcher>();
        if (!permissionMatcher.HasRequiredPermission(match.PermissionTemplates, snapshot.PermissionKeys, match.RouteValues))
        {
            return await DenyAsync(request,
                GatewayAuditActions.DenialReasons.InsufficientPermissions,
                "auth.insufficient_permissions",
                "Insufficient permissions",
                "User does not have required permissions for this endpoint");
        }

        var requestedProjectShortName = RequestedProjectShortName(match.RouteValues);
        var projectMembershipRequired = IsProjectMembershipRequired(snapshot, request.Services);

        if (string.IsNullOrEmpty(requestedProjectShortName)
            || !projectMembershipRequired
            || snapshot.UserProjectShortNames.Any(p => p.Equals(requestedProjectShortName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return await DenyAsync(request,
            GatewayAuditActions.DenialReasons.InsufficientProjectPermissions,
            "auth.insufficient_project_permissions",
            "Insufficient project permissions",
            "User does not have access to the requested project",
            requestedProjectShortName);
    }

    private static string? RequestedProjectShortName(IReadOnlyDictionary<string, string> routeValues)
    {
        var projectShortNameKey = routeValues.Keys.FirstOrDefault(k => k.Equals(ProjectShortNameRouteKey, StringComparison.OrdinalIgnoreCase));

        return projectShortNameKey is not null && routeValues.TryGetValue(projectShortNameKey, out var routeValue)
            ? routeValue
            : null;
    }

    // System roles (Platform Administrator, Provider Administrator) are cross-project by design and
    // are not project members, so the gateway leaves project-affiliation scoping to the API by default.
    // Set GatewayAuthorization:EnforceProjectMembershipForSystemRoles=true to also enforce it here for them.
    private static bool IsProjectMembershipRequired(UserPermissionsSnapshot snapshot, IServiceProvider serviceProvider)
    {
        var isSystemRole =
            string.Equals(snapshot.EffectiveRoleClaim, RoleInfoConstants.PlatformAdministratorRoleName, StringComparison.Ordinal) ||
            string.Equals(snapshot.EffectiveRoleClaim, RoleInfoConstants.ProviderAdministratorRoleName, StringComparison.Ordinal);
        var enforceForSystemRoles = serviceProvider
            .GetRequiredService<IOptions<GatewayAuthorizationOptions>>()
            .Value.EnforceProjectMembershipForSystemRoles;

        return enforceForSystemRoles || !isSystemRole;
    }

    private static async Task<bool> DenyAsync(
        AuthorizationRequest request, string reason, string problemCode, string title, string detail, string? project = null)
    {
        await AuditDenialAsync(request.Services, request.User, request.Path, request.Method, reason, project);
        await WriteForbiddenAsync(request.HttpContext, problemCode, title, detail);
        return false;
    }

    // Core normalizes the same claim in Caller.ResolveIdpId (src/Modules/Core/Shared/Auth/Caller.cs):
    // audit records from both sides are joined on ActorId, so the two must stay in step. The gateway
    // has no reference to Core, hence the duplicated split.
    private static string NormalizeActorId(string? sub)
    {
        if (string.IsNullOrEmpty(sub))
        {
            return AnonymousActorId;
        }

        var normalized = sub.Split('@')[0];
        return normalized.Length == 0 ? AnonymousActorId : normalized;
    }

    private static bool IsApiKeyCaller(ClaimsPrincipal user) =>
        user.FindFirstValue(EAuthorizationMethod.ClaimType) == nameof(EAuthorizationMethod.ApiKey);

    private static string ResolveActorType(ClaimsPrincipal user, string? sub)
    {
        if (IsApiKeyCaller(user))
        {
            return ServiceActorType;
        }

        return string.IsNullOrEmpty(sub) ? AnonymousActorType : UserActorType;
    }

    private static string TruncatePathForAudit(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var recorded = string.Concat(segments.Take(MaxAuditedPathSegments).Select(segment => '/' + segment));

        return segments.Length > MaxAuditedPathSegments ? recorded + PathTruncationMarker : recorded;
    }

    private static async Task AuditBypassAsync(IServiceProvider serviceProvider, ClaimsPrincipal user, string path, string method)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        await serviceProvider.GetRequiredService<IAuditLogger<AuditCategory>>().AuditAsync(
            AuditEvent.Success(GatewayAuditActions.AuthorizationBypassed, NormalizeActorId(sub), ResolveActorType(user, sub)) with
            {
                Reason = GatewayAuditActions.DenialReasons.PermissionServiceMissing,
                Data = AuditData(path, method, sub),
            });
    }

    private static async Task AuditDenialAsync(
        IServiceProvider serviceProvider, ClaimsPrincipal user, string path, string method, string reason, string? project = null)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var data = AuditData(path, method, sub);
        if (project is not null)
        {
            data["project"] = project;
        }

        await serviceProvider.GetRequiredService<IAuditLogger<AuditCategory>>().AuditAsync(
            AuditEvent.Denied(GatewayAuditActions.AuthorizationDenied, NormalizeActorId(sub), ResolveActorType(user, sub)) with
            {
                Reason = reason,
                Data = data,
            });
    }

    private static Dictionary<string, object?> AuditData(string path, string method, string? sub) => new()
    {
        ["path"] = TruncatePathForAudit(path),
        ["method"] = method,
        ["sub"] = sub,
    };

    private static Task WriteForbiddenAsync(HttpContext httpContext, string problemCode, string title, string detail)
        => ProblemResponse.WriteAsync(httpContext, StatusCodes.Status403Forbidden, problemCode, title, detail);

    private sealed record AuthorizationRequest(
        HttpContext HttpContext, ClaimsPrincipal User, IServiceProvider Services, string Path, string Method);
}