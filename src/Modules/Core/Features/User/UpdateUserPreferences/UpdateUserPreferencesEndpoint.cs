using System.Text.Json;

using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

namespace Core.Features.User.UpdateUserPreferences;

/// <summary>
/// Replaces the authenticated user's JSON preferences. Mirrors legacy <c>PUT api/User/Preferences</c>,
/// upserting the preference on first write. Returns 204 (legacy returned 200 with no body).
/// </summary>
internal static class UpdateUserPreferencesEndpoint
{
    internal sealed record Request(JsonElement Preferences);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        Caller caller,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (request.Preferences.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Problems.Validation(CommonErrorCodes.Required, "The 'Preferences' field is required.");

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } userId)
            return Problems.CallerNotProvisioned();

        var user = await users.FindAsync(caller.IdpId, ct);
        if (user is null)
            return Problems.CallerNotProvisioned();

        var update = user.UpdateUserPreferences(request.Preferences.GetRawText(), userId);
        if (!update.IsSuccess)
            return update.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await cache.EvictAsync(CacheTags.UserInfo(caller.IdpId.Value), ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(AuditActions.UserPreferencesUpdated, caller) with
        {
            TargetType = AuditActions.TargetTypes.User,
            TargetId = caller.IdpId.Value,
        }, logger);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateUserPreferencesEndpoint()
        {
            app.MapPut("Users/Me/Preferences", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateUserPreferencesV2")
            .WithSummary("Update current user preferences")
            .WithDescription("""Replaces the authenticated user's JSON preferences (created on first write). The body is a {"Preferences": {…}} envelope, so a GET response can be sent back unchanged. Returns 204; 400 when "Preferences" is missing or null; 404 when the caller is unknown.""")
            .WithTags("User")
            .Accepts<Request>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}