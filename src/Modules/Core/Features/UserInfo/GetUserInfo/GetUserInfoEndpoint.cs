using Core.Features.UserInfo.UseCases;
using Core.Shared.Http;

using Forge.Core.Features.Role.Services;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Core.Features.UserInfo.GetUserInfo;

internal static class GetUserInfoEndpoint
{
    internal static async Task<IResult> HandleAsync(
        Caller caller,
        [FromServices] IUserInfoQuery userDetailsQuery,
        [FromServices] IRoleListService roleListService,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<CallerProvisioning.AuditCategory> auditLogger,
        [FromServices] ILogger<CallerProvisioning.AuditCategory> logger,
        CancellationToken ct)
    {
        var result = await UserInfoLoader.LoadUserInfoAsync(userDetailsQuery, roleListService, users, unitOfWork, caller, auditLogger, logger, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : UserInfoLoader.ToProblem(result.Error);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetUserInfoEndpoint()
        {
            app.MapGet("UserInfo", HandleAsync)
                .CacheOutput(UserInfoCache.PolicyName)
                .WithETag()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetUserInfoV2")
            .WithSummary("Get user info from access token")
            .WithDescription("Returns LEXIS user info. Call on login to initialize/sync user.")
            .WithTags("UserInfo")
            .Produces<UserInfoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}