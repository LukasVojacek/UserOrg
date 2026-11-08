using Core.DataAccess.ResourceRequests;
using Core.Domain.Shared;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.Flow;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.ResourceRequest.ResendApprovalEmail;

/// <summary>
/// Re-sends the PI approval action link for a request still awaiting approval (route 6, ADR-0064). Project scope
/// is enforced on the route project (ADR-0048): a genuine miss, an out-of-scope caller, and a wrong-project
/// slug all return the byte-identical <c>resource_request.not_found</c> so existence never leaks (ADR-0018
/// rider). Valid only in <c>PendingPiApproval</c> (else 409). Mints a fresh single-use token, overwrites the
/// projection's <c>TokenHash</c>/<c>TokenExpiresAt</c> — which invalidates any outstanding link — and re-emails
/// the PI. The gate stays parked; there is no engine interaction.
/// </summary>
/// <remarks>
/// Unintended side effect of going through <see cref="IResourceRequestProjectionStore.UpdateBySlugAsync"/>: it
/// stamps <c>UpdatedAt</c> on every mutation, which is also the sweeper's expiry clock (DEC-7,
/// <see cref="Core.Features.ResourceRequest.Expiry.ResourceRequestExpiryOptions"/>). A resend therefore restarts
/// the 30-day expiry window for the <c>PendingPiApproval</c> gate, not just the link's own TTL — current
/// behaviour observed in the implementation, not a designed guarantee.
/// </remarks>
internal static class ResendApprovalEmailEndpoint
{
    internal sealed record Request([FromRoute] string ProjectShortName, [FromRoute] string Slug);

    private const string NotFoundDetail = "Request not found.";

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IProjectResourceRequestQuery query,
        [FromServices] IResourceRequestProjectionStore store,
        [FromServices] INotificationSender notifications,
        [FromServices] IOptions<ResourceRequestActionLinkOptions> options,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        if (!ProjectShortName.TryParse(request.ProjectShortName, null, out var routeShortName))
            return NotFound();

        // A missing row, an out-of-scope route project, and a wrong-project slug are all indistinguishable.
        var detail = await query.FindBySlugAsync(request.Slug, ct);
        if (detail is null)
            return NotFound();                                          // genuine miss
        if (!caller.Scope.Contains(routeShortName))
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ResourceRequestApprovalLinkReissued, caller, routeShortName) with
            {
                TargetType = AuditActions.TargetTypes.ResourceRequest,
                TargetId = request.Slug,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return NotFound();
        }
        if (detail.ProjectShortName != routeShortName.Value)
            return NotFound();                                          // wrong-project — SAME problem

        if (ResourceRequestState.From(detail.State) != ResourceRequestState.PendingPiApproval)
            return Problems.Conflict(ResourceRequestErrorCodes.NotResendable, "The request is not awaiting PI approval.");

        var token = ResourceRequestToken.Generate();
        var hash = ResourceRequestToken.Hash(token);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(options.Value.DurationInHours);

        // Overwriting the hash + expiry invalidates the previously emailed link (single-use, ADR-0064).
        await store.UpdateBySlugAsync(request.Slug, r =>
        {
            r.TokenHash = hash;
            r.TokenExpiresAt = expiresAt;
        }, ct);

        await notifications.SendPiApprovalRequestedAsync(
            Email.Create(detail.ContactEmail),
            detail.Slug,
            token,
            ProjectShortName.Create(detail.ProjectShortName),
            ResourceName.Create(detail.ResourceName),
            ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ResourceRequestApprovalLinkReissued, caller, routeShortName,
            new Dictionary<string, object?> { ["token_hash"] = hash, ["token_expires_at"] = expiresAt }) with
        {
            TargetType = AuditActions.TargetTypes.ResourceRequest,
            TargetId = request.Slug,
        });

        return Results.NoContent();
    }

    private static IResult NotFound() =>
        Problems.NotFound(ResourceRequestErrorCodes.NotFound, NotFoundDetail);

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapResendApprovalEmailEndpoint()
        {
            app.MapPost("Projects/{projectShortName}/ResourceRequests/{slug}/ResendApproval", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("ResendResourceRequestApprovalV2")
            .WithSummary("Re-send the PI approval link for a resource-assignment request")
            .WithDescription("Regenerates the PI approval action link for a request still awaiting approval, invalidating the previous link, and re-emails the PI. Caller must hold a role on the route project. Returns 204; 404 (resource_request.not_found) when the request is unknown, outside the caller's scope, or belongs to a different project — the three are deliberately indistinguishable; 409 (resource_request.not_resendable) when the request is not awaiting PI approval. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("ResourceRequest")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
    }
}