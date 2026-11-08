using Core.Features.UserInfo.GetExtendedUserInfo;
using Core.Features.UserInfo.GetUserInfo;
using Core.Features.UserInfo.UseCases;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Core.Features.UserInfo;

/// <summary>
/// Builds the UserInfo responses claims-first (ADR-0006): the effective role and project scope come
/// from the <see cref="Caller"/> (the internal token's <c>role</c> and <c>projectRoles</c> claims).
/// The role service is still consulted — only to expand a role name into its full permission list,
/// which is not carried on the token.
///
/// Provisions the caller just-in-time before projecting (ADR-0068): <c>Load*Async</c> fails with the
/// <see cref="CallerProvisioning"/> error when the token cannot support provisioning (no <c>email</c> or
/// <c>preferred_username</c>) or the caller has been deactivated — the endpoint translates that into
/// 404 <c>caller.not_provisioned</c> / 403 <c>user.deactivated</c> via <see cref="ToProblem"/>, never a 500.
/// </summary>
internal static class UserInfoLoader
{
    internal static IResult ToProblem(Error error) =>
        error.Code == UserErrorCodes.Deactivated ? Problems.CallerDeactivated() : Problems.CallerNotProvisioned();

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetRoleDefinitionsAsync(
        IRoleListService roleListService,
        CancellationToken ct)
    {
        var rolesResult = await roleListService.ListRoles(ct);
        if (!rolesResult.IsSuccess)
            throw new InvalidOperationException("Failed to retrieve roles from Role Service.");

        return rolesResult.Value.ToDictionary(
            r => r.Name,
            r => (IReadOnlyList<string>)r.Permissions.Select(p => p.Key.Value).ToList());
    }

    private static Dictionary<string, HashSet<ProjectShortName>> ToProjectRoles(Caller caller)
        => caller.ProjectRoles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToHashSet());

    internal static async Task<Result<UserInfoResponse, Error>> LoadUserInfoAsync(
        IUserInfoQuery userDetailsQuery,
        IRoleListService roleListService,
        IRepository<Domain.User.User, IdpId> users,
        IUnitOfWork unitOfWork,
        Caller caller,
        IAuditLogger<CallerProvisioning.AuditCategory> auditLogger,
        ILogger<CallerProvisioning.AuditCategory> logger,
        CancellationToken ct)
    {
        var provisioned = await CallerProvisioning.ProvisionAsync(caller, users, userDetailsQuery, unitOfWork, auditLogger, logger, ct);
        if (!provisioned.IsSuccess)
            return Result<UserInfoResponse, Error>.Failure(provisioned.Error);

        // Guaranteed present: ProvisionAsync just found-or-created and saved this exact IdpId.
        var user = (await userDetailsQuery.GetBySubAsync(caller.IdpId, ct))!;

        var roleDefinitions = await GetRoleDefinitionsAsync(roleListService, ct);

        return Result<UserInfoResponse, Error>.Success(
            UserInfoProjection.BuildUserInfo(user, caller.Name, caller.Role, roleDefinitions, ToProjectRoles(caller)));
    }

    internal static async Task<Result<UserInfoExtendedResponse, Error>> LoadExtendedUserInfoAsync(
        IUserInfoQuery userDetailsQuery,
        IRoleListService roleListService,
        IRepository<Domain.User.User, IdpId> users,
        IUnitOfWork unitOfWork,
        Caller caller,
        IAuditLogger<CallerProvisioning.AuditCategory> auditLogger,
        ILogger<CallerProvisioning.AuditCategory> logger,
        CancellationToken ct)
    {
        var provisioned = await CallerProvisioning.ProvisionAsync(caller, users, userDetailsQuery, unitOfWork, auditLogger, logger, ct);
        if (!provisioned.IsSuccess)
            return Result<UserInfoExtendedResponse, Error>.Failure(provisioned.Error);

        // Guaranteed present: ProvisionAsync just found-or-created and saved this exact IdpId.
        var user = (await userDetailsQuery.GetExtendedBySubAsync(caller.IdpId, ct))!;

        var roleDefinitions = await GetRoleDefinitionsAsync(roleListService, ct);

        return Result<UserInfoExtendedResponse, Error>.Success(
            UserInfoExtendedProjection.BuildExtendedUserInfo(user, caller.Name, caller.Role, roleDefinitions, ToProjectRoles(caller)));
    }
}