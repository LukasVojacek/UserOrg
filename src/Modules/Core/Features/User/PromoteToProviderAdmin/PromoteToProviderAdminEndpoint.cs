using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using DomainUser = Core.Domain.User.User;

namespace Core.Features.User.PromoteToProviderAdmin;

/// <summary>
/// Promotes a user to the ProviderAdmin system role, bound to a single provider (Admin only). Mirrors legacy
/// <c>PUT api/Administration/PromoteToProviderAdmin</c>, but addresses the user by IdP subject and the provider
/// by its natural name (ADR-0024/0029). The provider's surrogate id is resolved by a read query and passed into
/// the synchronous <see cref="DomainUser.PromoteToProviderAdmin"/> aggregate method (ADR-0032).
/// </summary>
internal static class PromoteToProviderAdminEndpoint
{
    internal sealed record Request(
        [FromRoute] string IdpUserId,
        [FromRoute] string ProviderName);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<DomainUser, IdpId> users,
        [FromServices] IProviderQuery providers,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        // Changing a user's platform/system role is platform maintenance, not a project member's right — Admin only.
        if (!caller.IsAdmin(roleOptions.Value))
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(AuditActions.UserProviderAdminGranted, caller) with
            {
                TargetType = AuditActions.TargetTypes.User,
                TargetId = request.IdpUserId,
                Reason = AuditActions.DenialReasons.UserRoleWriteForbidden,
            });
            return Problems.Forbidden("user.role_write_forbidden", "Only an administrator may change a user's system role.");
        }

        var user = await users.FindAsync(new IdpId(request.IdpUserId), ct);
        if (user is null)
            return Problems.NotFound("user.not_found", "The target user does not exist.");

        if (!DomainProviderName.TryCreate(request.ProviderName, out var providerName, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        var providerId = await providers.FindActiveProviderIdByNameAsync(providerName, ct);
        if (providerId is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        if (user.PromoteToProviderAdmin(new HashSet<ProviderId> { providerId.Value }) is { IsSuccess: false } result)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.UserProviderAdminGranted, caller,
            data: new Dictionary<string, object?> { ["provider_name"] = providerName.Value }) with
        {
            TargetType = AuditActions.TargetTypes.User,
            TargetId = request.IdpUserId,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapPromoteToProviderAdminEndpoint()
        {
            app.MapPut("Users/{idpUserId}/ProviderAdmin/{providerName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("PromoteToProviderAdminV2")
            .WithSummary("Promote a user to ProviderAdmin")
            .WithDescription("Promotes a user to the ProviderAdmin system role bound to the named provider (Admin only). Returns 204; 403 for non-admins; 404 when the user or provider does not exist; 409 when the target is an administrator (the Admin role cannot be changed).")
            .WithTags("User")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}