using UserOrganizationManagement.Application.LogMessages;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.API.Authentication;

internal sealed class UserInitializationEndpointFilter(IUserService userService, ILogger<UserInitializationEndpointFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!context.HttpContext.Request.Path.StartsWithSegments("/api/UserInfo", StringComparison.InvariantCultureIgnoreCase))
        {
            return await next(context);
        }

        var openIdInfoRetrieved = context.HttpContext.TryGetOpenIdUserInfoModel(out var openIdInfo);
        if (!openIdInfoRetrieved || openIdInfo is null)
        {
            logger.InvalidOpenIdInfo(openIdInfo);
            return await next(context);
        }
        _ = await CreateOrUpdateUser();

        return await next(context);
        #region LocalMethods

        async Task<User> CreateOrUpdateUser()
        {
            var cancelationToken = context.HttpContext.RequestAborted;
            // Identify the user by idpid (sub), consistent with the rest of the app and the v1
            // UserInfo body (UserService:199, AuthenticationExtensions:320). Normalize the '@' suffix
            // the same way so the lookup key matches how IdentityProviderUserId is stored.
            var idpUserId = openIdInfo.Sub.Split('@')[0];
            var dbUser = await userService.GetUserBySidAsync(idpUserId, cancelationToken);

            if (dbUser is null)
            {
                dbUser = new User()
                {
                    Email = openIdInfo.Email,
                    IdentityProviderUserId = idpUserId,
                    UserName = openIdInfo.PreferredUsername
                };

                await userService.CreateUser(dbUser, cancelationToken);
                logger.UserCreated(dbUser.UserName);
            }

            if (!string.IsNullOrEmpty(openIdInfo.Email) && dbUser.Email != openIdInfo.Email)
            {
                dbUser.Email = openIdInfo.Email;

                await userService.UpdateUser(dbUser);
                logger.UserUpdated(dbUser.UserName);
            }

            return dbUser;
        }

        #endregion
    }
}