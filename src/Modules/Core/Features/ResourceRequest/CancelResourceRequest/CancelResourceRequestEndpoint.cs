using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest.Flow;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.Features.ResourceRequest.CancelResourceRequest;

/// <summary>
/// Cancels a resource-assignment request (route 5, ADR-0064). Project scope is enforced on the route project
/// (ADR-0048): a genuine miss, an out-of-scope caller, and a wrong-project slug all return the byte-identical
/// <c>resource_request.not_found</c> so existence never leaks (ADR-0018 rider). The endpoint resumes whichever
/// gate the request is parked at with a cancel signal — <c>PiDecision.Cancel</c> from <c>PendingPiApproval</c>
/// or <c>ResourceAvailabilitySignal.Cancel</c> from <c>AwaitingResourceCreation</c> — and the flow's
/// <c>InvalidateLinks</c> step writes the terminal <c>Cancelled</c> projection state. A request in a terminal
/// state is a 409; the engine not yet parked at its gate is a deterministic 409
/// <c>resource_request.not_cancellable_yet</c> (retry).
/// </summary>
internal static class CancelResourceRequestEndpoint
{
    internal sealed record Request([FromRoute] string ProjectShortName, [FromRoute] string Slug);

    internal sealed class AuditCategory;

    private const string NotFoundDetail = "Request not found.";

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IProjectResourceRequestQuery query,
        [FromServices] IResourceRequestProjectionStore store,
        [FromServices] IWorkflowClient workflow,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
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
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(AuditActions.ResourceRequestSelfCancelled, caller, routeShortName) with
            {
                TargetType = AuditActions.TargetTypes.ResourceRequest,
                TargetId = request.Slug,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            }, logger);
            return NotFound();                                          // out-of-scope - SAME problem
        }
        if (detail.ProjectShortName != routeShortName.Value)
            return NotFound();                                          // wrong-project — SAME problem

        var state = ResourceRequestState.From(detail.State);

        // Orphan row: inserted, but the workflow never started (no distributed transaction — the process can
        // die between the insert and StartAsync). There is nothing to resume, and leaving it active wedges
        // the single-active guard forever, so settle it here and release the slug.
        if (detail.WorkflowRequestId == Guid.Empty && IsParked(state))
        {
            await store.UpdateBySlugAsync(request.Slug, r =>
            {
                r.State = ResourceRequestState.Cancelled;
                r.TokenHash = null;
                r.TokenExpiresAt = null;
            }, ct);

            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
                AuditActions.ResourceRequestSelfCancelled, caller, routeShortName,
                new Dictionary<string, object?> { ["orphan"] = true }) with
            {
                TargetType = AuditActions.TargetTypes.ResourceRequest,
                TargetId = request.Slug,
            }, logger);
            return Results.NoContent();
        }

        var cancelled = CallerAuditMapper.ComposeSuccess(
            AuditActions.ResourceRequestSelfCancelled, caller, routeShortName) with
        {
            TargetType = AuditActions.TargetTypes.ResourceRequest,
            TargetId = request.Slug,
        };
        return await ResumeCancelAsync(workflow, detail.WorkflowRequestId, state, auditLogger, logger, cancelled, ct);
    }

    private static bool IsParked(ResourceRequestState state) =>
        state == ResourceRequestState.PendingPiApproval || state == ResourceRequestState.AwaitingResourceCreation;

    private static async Task<IResult> ResumeCancelAsync(
        IWorkflowClient workflow,
        Guid workflowRequestId,
        ResourceRequestState state,
        IAuditLogger<AuditCategory> auditLogger,
        ILogger<AuditCategory> logger,
        AuditEvent cancelled,
        CancellationToken ct)
    {
        ResumeResult resume;
        if (state == ResourceRequestState.PendingPiApproval)
        {
            resume = await workflow.ResumeAsync(
                workflowRequestId, new PiDecision(PiDecisionKind.Cancel), ct);
        }
        else if (state == ResourceRequestState.AwaitingResourceCreation)
        {
            resume = await workflow.ResumeAsync(
                workflowRequestId, new ResourceAvailabilitySignal(ResourceAvailabilityKind.Cancel), ct);
        }
        else
        {
            return Problems.Conflict(ResourceRequestErrorCodes.NotCancellable, "The request is no longer active.");
        }

        // The projection state says the request is parked, but the engine has not yet reached the gate — an
        // expected race on a freshly-created request. Deterministic answer: retryable 409 (ADR-0064).
        if (resume.Outcome != ResumeOutcome.Resumed)
            return Problems.Conflict(ResourceRequestErrorCodes.NotCancellableYet, "The request is not ready to be cancelled yet; retry shortly.");

        await auditLogger.AuditBestEffortAsync(cancelled, logger);
        return Results.NoContent();
    }

    private static IResult NotFound() =>
        Problems.NotFound(ResourceRequestErrorCodes.NotFound, NotFoundDetail);

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCancelResourceRequestEndpoint()
        {
            app.MapPut("Projects/{projectShortName}/ResourceRequests/{slug}/Cancel", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CancelResourceRequestV2")
            .WithSummary("Cancel a resource-assignment request")
            .WithDescription("Cancels a resource-assignment request by resuming whichever approval gate it is parked at with a cancel signal; the request terminates as Cancelled. Caller must hold a role on the route project. Returns 204; 404 (resource_request.not_found) when the request is unknown, outside the caller's scope, or belongs to a different project — the three are deliberately indistinguishable; 409 when the request is already terminal (resource_request.not_cancellable) or the workflow is not yet parked at its gate (resource_request.not_cancellable_yet, retryable). On a failure read the 'code' extension; do not parse the message.")
            .WithTags("ResourceRequest")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);
    }
}