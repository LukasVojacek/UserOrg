using System.Security.Cryptography;
using System.Text;

using Forge.Core.Features.UserOrgRoles;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.Application.LogMessages;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.API;

internal sealed class UserInfoEndpoint : IApiRoute
{
    internal static string BuildUserPermissionsCacheVersion(IEnumerable<UserPermissionDto> userPermissions)
    {
        ArgumentNullException.ThrowIfNull(userPermissions);

        var normalizedPermissions = userPermissions
            .OrderBy(permission => permission.SystemRoleId)
            .ThenBy(permission => permission.SystemRoleName, StringComparer.Ordinal)
            .ThenBy(permission => permission.ProjectId)
            .ThenBy(permission => permission.ShortName, StringComparer.Ordinal)
            .Select(permission => $"{permission.SystemRoleId}|{permission.SystemRoleName}|{permission.ProjectId}|{permission.ShortName}");

        var payload = string.Join('\n', normalizedPermissions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static string BuildUserInfoCacheKey(string subjectClaim, string permissionsVersion) => $"UserInfoModel_{subjectClaim}_{permissionsVersion}";

    internal static string BuildExtendedUserInfoCacheKey(string subjectClaim, string permissionsVersion) => $"UserInfoModel_Extended_{subjectClaim}_{permissionsVersion}";

    private static void TriggerClusterCacheWarmUp(HttpContext context, IClusterInfoCacheWarmer clusterCacheWarmer, string subjectClaim, IReadOnlyDictionary<string, HashSet<ProjectShortName>> projectRoles)
    {
        if (!context.TryGetOriginalAuthToken(out var originalToken))
        {
            return;
        }

        var projects = projectRoles.Values.SelectMany(p => p).Distinct().ToList();
        clusterCacheWarmer.WarmInBackground(subjectClaim, projects, originalToken);
    }

    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("UserInfo")
            .WithTags("UserInfo");

        group.MapGet("", async ([FromServices] IUserService service, [FromServices] HybridCache cache, [FromServices] IUserOrgRolesService rolesService, [FromServices] IClusterInfoCacheWarmer clusterCacheWarmer, [FromServices] ILogger<UserInfoEndpoint> logger, IConfiguration configuration, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetAuthToken(out var token))
            {
                throw new BearerTokenNotFoundException();
            }

            var openIdInfoAvailable = context.TryGetOpenIdUserInfoModel(out var openIdInfo);
            if (!openIdInfoAvailable)
            {
                logger.FailedToRetrieveOpenIdInfoLog();
                return Results.Problem("Invalid token", statusCode: StatusCodes.Status401Unauthorized);
            }

            var userPermissions = await rolesService.GetUserPermissionsAsync(openIdInfo.Sub, null, ct);
            var permissionsVersion = BuildUserPermissionsCacheVersion(userPermissions);
            var projectRoles = userPermissions.ToProjectRecords(configuration, out _)
                .ToDictionary(
                    k => k.Key,
                    v => v.Value.Select(p => ProjectShortName.Create(p)).ToHashSet());

            // Include system-level roles (ProjectId == null) as empty-set entries so that
            // PopulateUserInfo can correctly resolve the user's platform role (Admin/ProviderAdmin/User).
            // ToProjectRecords skips these roles from the dictionary to avoid project cache-warmer issues.
            foreach (var systemRoleName in userPermissions
                .Where(p => p.ProjectId == null)
                .Select(p => p.SystemRoleName)
                .Distinct())
            {
                projectRoles.TryAdd(systemRoleName, []);
            }

            var result = await cache.GetOrCreateAsync(BuildUserInfoCacheKey(openIdInfo.Sub, permissionsVersion), async entry =>
            {
                var data = await service.PopulateUserInfo(openIdInfo, projectRoles, ct);
                return data;
            }, options: new HybridCacheEntryOptions()
            {
                Expiration = TimeSpan.FromMinutes(3)
            }, tags: [$"UserInfoModel_{openIdInfo.Sub}"], cancellationToken: ct);

            TriggerClusterCacheWarmUp(context, clusterCacheWarmer, openIdInfo.Sub, projectRoles);

            return Results.Ok(result);

        }).Produces<UserInfoModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS User" information about user from access token""";
              generatedOperation.Description = """Get "LEXIS User" information about user from access token. This should be called every time when user is logged in WebGui to make sure internal database User entity is initialized and synchronized with data from Keycloak.""";
              return generatedOperation;
          });

        group.MapGet("Extended", async ([FromServices] IUserService service, [FromServices] HybridCache cache, [FromServices] IUserOrgRolesService rolesService, [FromServices] IClusterInfoCacheWarmer clusterCacheWarmer, [FromServices] ILogger<UserInfoEndpoint> logger, HttpContext context, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!context.TryGetAuthToken(out var token))
            {
                throw new BearerTokenNotFoundException();
            }

            var openIdInfoAvailable = context.TryGetOpenIdUserInfoModel(out var openIdInfo);
            if (!openIdInfoAvailable)
            {
                logger.FailedToRetrieveOpenIdInfoLog();
                return Results.Problem("Invalid token", statusCode: StatusCodes.Status401Unauthorized);
            }
            var subjectClaim = openIdInfo.Sub;
            var userPermissions = await rolesService.GetUserPermissionsAsync(subjectClaim, null, ct);
            var permissionsVersion = BuildUserPermissionsCacheVersion(userPermissions);
            var projectRoles = userPermissions.ToProjectRecords(configuration, out _)
                .ToDictionary(
                    k => k.Key,
                    v => v.Value.Select(p => ProjectShortName.Create(p)).ToHashSet());

            // Include system-level roles (ProjectId == null) as empty-set entries so that
            // PopulateExtendedUserInfo can correctly resolve the user's platform role (Admin/ProviderAdmin/User).
            // ToProjectRecords skips these roles from the dictionary to avoid project cache-warmer issues.
            foreach (var systemRoleName in userPermissions
                .Where(p => p.ProjectId == null)
                .Select(p => p.SystemRoleName)
                .Distinct())
            {
                projectRoles.TryAdd(systemRoleName, []);
            }

            var result = await cache.GetOrCreateAsync(BuildExtendedUserInfoCacheKey(openIdInfo.Sub, permissionsVersion), async entry =>
            {
                return await service.PopulateExtendedUserInfo(openIdInfo, projectRoles, ct);
            }, options: new HybridCacheEntryOptions()
            {
                Expiration = TimeSpan.FromMinutes(3)
            }, tags: [$"UserInfoModel_Extended_{openIdInfo.Sub}"], cancellationToken: ct);

            TriggerClusterCacheWarmUp(context, clusterCacheWarmer, openIdInfo.Sub, projectRoles);

            return Results.Ok(result);

        }).Produces<UserInfoExtendedModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get extended "LEXIS User" information about user from access token""";
              generatedOperation.Description = """Get extended "LEXIS User" information about user from access token.""";
              return generatedOperation;
          });
    }
}