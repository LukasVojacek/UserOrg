using Core.DataAccess.Queries.Contracts;
using Core.Domain.User;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using RemI.Features.Projects;
using RemI.Features.Users;
using RemI.HostContracts;

using UserOrganizationManagement.Domain.IRepositories;

namespace Userorg.Adapter.RemI.Userorg;

internal sealed class UserorgService(
    IUserInfoQuery userInfoQuery,
    IProjectQuery projectQuery,
    IRoleListService roleListService,
    IUserPermissionRepository userPermissionRepository)
    : IUserorgService
{
    // why: the RemI routes publish one unpaged list while the queries behind them are offset-paged, so the pages are drained here instead of asking the database for a single unbounded one
    private const int PageSize = 200;

    public async Task<Result<IReadOnlyList<UserResponse>, Error>> GetUsersAsync(CancellationToken ct)
    {
        try
        {
            var users = await userInfoQuery.GetUsersAsync(ct);
            if (users == null)
            {
                return Result<IReadOnlyList<UserResponse>, Error>.Failure(
                    Errors.UnexpectedErrorRetrievingUsers("User list is null"), State.CriticalError);
            }

            var result = users.Select(u => u.ConvertToModel())
                .ToList();

            return Result<IReadOnlyList<UserResponse>, Error>.Success(result, State.Ok);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<UserResponse>, Error>.Failure(
                Errors.UnexpectedErrorRetrievingUsers(ex.Message), State.CriticalError);
        }
    }

    public async Task<Result<UserSystemRolesResponse, Error>> GetUserSystemRoles(string idpUserId, CancellationToken ct)
    {
        try
        {
            var idpId = IdpId.From(idpUserId);
            var userInfo = await userInfoQuery.GetBySubAsync(idpId, ct);
            if (userInfo == null)
            {
                return Result<UserSystemRolesResponse, Error>.Failure(
                    Errors.UnexpectedErrorRetrievingUser(idpUserId), State.NotFound);
            }

            var roles = await roleListService.ListRoles(ct);
            if (!roles.IsSuccess)
                return Result<UserSystemRolesResponse, Error>.Failure(
                    Errors.UnexpectedErrorRetrievingUserSystemRoles(roles.Error.Description), State.CriticalError);

            var userPermissions = await userPermissionRepository.GetPermissionsForUserAsync(idpUserId, ct);

            return Result<UserSystemRolesResponse, Error>.Success(
                new UserSystemRolesResponse(
                    idpUserId,
                    userInfo.UserName.ToString(),
                    userPermissions.Select(p =>
                    new ProjectSystemRoleRecord(
                        roles.Value.First(f => f.Id == p.SystemRoleId!.Value).Name,
                        p.Project?.ShortName,
                        roles.Value.First(f => f.Id == p.SystemRoleId!.Value).Permissions.Select(s => s.Key.Value).ToList()
                    )).ToList()
                ), State.Ok);
        }
        catch (Exception ex)
        {
            return Result<UserSystemRolesResponse, Error>.Failure(
                Errors.UnexpectedErrorRetrievingUserSystemRoles(ex.Message), State.CriticalError);
        }
    }

    public async Task<Result<IReadOnlyList<ProjectResponse>, Error>> GetProjectsAsync(CancellationToken ct)
    {
        try
        {
            var filter = new ProjectFilter(null, null, null, null, null);
            List<ProjectResponse> projects = [];

            for (var page = 1; ; page++)
            {
                var queryPage = await projectQuery.GetProjectsAsync(filter, page, PageSize, ct);
                projects.AddRange(queryPage.Items.Select(p => p.ConvertToModel()));

                if (queryPage.Items.Count < PageSize || projects.Count >= queryPage.TotalItems)
                {
                    break;
                }
            }

            return Result<IReadOnlyList<ProjectResponse>, Error>.Success(projects, State.Ok);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ProjectResponse>, Error>.Failure(
                Errors.UnexpectedErrorRetrievingProjects(ex.Message), State.CriticalError);
        }
    }
}