using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using DomainUser = Core.Domain.User.User;

namespace Core.Features.User.ResetToPlatformUser;

/// <summary>
/// Resets a user from ProviderAdmin back to the platform User role (Admin only). Mirrors legacy
/// <c>PUT api/Administration/ResetToPlatformUser</c> as a one-call DELETE. The aggregate refuses to demote while a
/// provider is still assigned, so the use-case first unassigns every provider, then demotes, in one transaction
/// (ADR-0032). Both mutations raise <c>IUserScopedEvent</c>s, so the user's cache is evicted automatically.
/// </summary>
internal static class ResetProviderAdminEndpoint
{
    internal sealed record Request([FromRoute] string IdpUserId);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<DomainUser, IdpId> users,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        if (!caller.IsAdmin(roleOptions.Value))
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(AuditActions.UserProviderAdminRevoked, caller) with
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

        string[] providerIds = [.. user.AssignedProviderIds.Select(id => id.Value.ToString())];

        // Demote refuses while a provider is still assigned; unassign first so a single DELETE fully reverts the
        // user to a platform user (parity with the legacy one-step reset).
        foreach (var providerId in user.AssignedProviderIds)
        {
            if (user.UnassignProvider(providerId) is { IsSuccess: false } unassigned)
                return unassigned.Error.ToProblem();
        }

        if (user.ResetToPlatformUser() is { IsSuccess: false } result)
            return result.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.UserProviderAdminRevoked, caller,
            data: new Dictionary<string, object?> { ["provider_ids"] = providerIds }) with
        {
            TargetType = AuditActions.TargetTypes.User,
            TargetId = request.IdpUserId,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapResetProviderAdminEndpoint()
        {
            app.MapDelete("Users/{idpUserId}/ProviderAdmin", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("ResetToPlatformUserV2")
            .WithSummary("Reset a user to platform User")
            .WithDescription("Resets a user from ProviderAdmin back to the platform User role, unassigning their provider(s) first (Admin only). Returns 204; 403 for non-admins; 404 when the user does not exist; 409 when the target is an administrator (the Admin role cannot be changed).")
            .WithTags("User")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}