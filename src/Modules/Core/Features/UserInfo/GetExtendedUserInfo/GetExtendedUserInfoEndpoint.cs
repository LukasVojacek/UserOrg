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

namespace Core.Features.UserInfo.GetExtendedUserInfo;

internal static class GetExtendedUserInfoEndpoint
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
        var result = await UserInfoLoader.LoadExtendedUserInfoAsync(
            userDetailsQuery, roleListService, users, unitOfWork, caller, auditLogger, logger, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : UserInfoLoader.ToProblem(result.Error);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetExtendedUserInfoEndpoint()
        {
            app.MapGet("UserInfo/Extended", HandleAsync)
                .CacheOutput(UserInfoCache.PolicyName)
                .WithETag()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetExtendedUserInfoV2")
            .WithSummary("Get extended user info from access token")
            .WithDescription("Returns extended LEXIS user info including project resources and HPC endpoint locations. Call on login alongside GetUserInfo.")
            .WithTags("UserInfo")
            .Produces<UserInfoExtendedResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}