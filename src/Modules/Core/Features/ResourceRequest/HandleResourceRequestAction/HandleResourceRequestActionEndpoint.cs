using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.PublicAction;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.Features.ResourceRequest.HandleResourceRequestAction;

/// <summary>
/// Settles a resource request through the single-use PI action-link token, without authentication (ADR-0064):
/// the token authorises delivering the PI's <c>Approve</c>/<c>Reject</c> decision to the flow's <c>PiApproval</c>
/// gate. An unknown/expired/already-used token — or a request the engine no longer awaits (already resumed, cancelled
/// elsewhere) — returns the uniform 404 so no request state ever leaks. Cancel is intentionally not accepted here;
/// it belongs to the authenticated Owner/Manager cancel endpoint (B07).
/// </summary>
internal static class HandleResourceRequestActionEndpoint
{
    internal sealed record Request(
        [FromRoute] string Token,
        [FromBody] Body Body);

    internal sealed record Body(string Decision, string? Reason = null);

    // The endpoint itself is a static class and cannot be used as a generic type argument, so this
    // otherwise-unused marker gives IAuditLogger<T> a category to bind to.
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IProjectResourceRequestQuery query,
        [FromServices] IWorkflowClient workflow,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        // Resolve the token before parsing the decision, so a bad decision on an invalid token 404s identically
        // and cannot be told apart from a valid token (ADR-0064 anti-leak).
        var detail = await query.FindByActionTokenAsync(request.Token, ct);
        if (detail is null)
        {
            // No approver identity is ever known on this anonymous path (ADR-0064) — not even here, where the token
            // itself could not be resolved to a request.
            await auditLogger.AuditAsync(ActionLinkAudit.Denied(
                AuditActions.ResourceRequestActionSettled, request.Token, ResourceRequestActionProblems.ActionNotFoundCode));
            return ResourceRequestActionProblems.ActionNotFound();
        }

        if (!TryParseDecision(request.Body.Decision, out var kind))
            return Problems.Validation(
                ResourceRequestActionProblems.InvalidDecisionCode,
                "Decision must be 'Approve' or 'Reject'.");

        // The reason is persisted to the projection and emailed to the requester, so it must be bounded to keep this
        // anonymous surface from being used for storage/log/email flooding. Checked only after the token resolves,
        // so a holder of an invalid token still sees the identical 404 (ADR-0064 anti-leak) and never this 400.
        if (request.Body.Reason?.Length > ResourceRequestActionProblems.MaxReasonLength)
            return Problems.Validation(
                ResourceRequestActionProblems.ReasonTooLongCode,
                $"Reason must be at most {ResourceRequestActionProblems.MaxReasonLength} characters.");

        // Reason is only meaningful on rejection (surfaced to the requester); ignored for approvals.
        var reason = kind == PiDecisionKind.Reject ? request.Body.Reason : null;
        var signal = new PiDecision(kind, reason);

        var resume = await workflow.ResumeAsync<PiDecision>(detail.WorkflowRequestId, signal, ct);
        if (resume.Outcome != ResumeOutcome.Resumed)
        {
            await auditLogger.AuditAsync(ActionLinkAudit.Denied(
                AuditActions.ResourceRequestActionSettled, request.Token, AuditActions.DenialReasons.ResourceRequestNotAwaitingDecision));
            return ResourceRequestActionProblems.ActionNotFound();
        }

        // The requester, not the approver, is the only identity this record carries: possession of the token is all
        // that is ever known about who clicked it (ADR-0064 — see the class summary).
        await auditLogger.AuditAsync(ActionLinkAudit.Success(
            AuditActions.ResourceRequestActionSettled, request.Token, detail.ProjectShortName,
            new Dictionary<string, object?> { ["slug"] = detail.Slug, ["requester_idp_id"] = detail.RequesterIdpId }));

        return Results.NoContent();
    }

    private static bool TryParseDecision(string? value, out PiDecisionKind kind)
    {
        if (string.Equals(value, nameof(PiDecisionKind.Approve), StringComparison.OrdinalIgnoreCase))
        {
            kind = PiDecisionKind.Approve;
            return true;
        }

        if (string.Equals(value, nameof(PiDecisionKind.Reject), StringComparison.OrdinalIgnoreCase))
        {
            kind = PiDecisionKind.Reject;
            return true;
        }

        kind = default;
        return false;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapHandleResourceRequestActionEndpoint()
        {
            app.MapPost("Public/ResourceRequestActions/{token}", HandleAsync)
                .AllowAnonymous()
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("HandleResourceRequestActionV2")
            .WithSummary("Settle a resource request via its action link")
            .WithDescription("Delivers the PI's Approve/Reject decision (with an optional reason on reject) through a single-use action-link token. No authentication required. Returns 204 when the decision is accepted; an identical 404 (code resource_request.action_not_found) for any unknown, expired, already-used, or already-settled token; 400 (code resource_request.invalid_decision) for a decision other than Approve or Reject.")
            .WithTags("Public")
            .Accepts<Body>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}