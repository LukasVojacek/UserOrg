using Core.Features.Request.Notifications;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Extensions;

using RequestAggregate = Core.Domain.Request.Request;

namespace Core.Features.Request.RejectRequest;

/// <summary>
/// Rejects a pending self-onboarding request (terminal). Mirrors legacy
/// <c>PUT api/Requests/{requestId}/Reject</c>. Project scope is enforced in Core (ADR-0009): the caller
/// must hold a role on the request's own project. The optional reason is accepted for API parity but,
/// as in legacy, is not persisted.
/// </summary>
internal static class RejectRequestEndpoint
{
    internal sealed record Request([FromRoute] string Slug, [FromQuery] string? Reason);

    // A genuinely-missing request and one outside the caller's scope MUST return the identical problem so
    // existence never leaks (ADR-0018 rider). Both paths use this single code + detail.
    private const string NotFoundDetail = "Request not found.";

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRequestQuery requestQuery,
        [FromServices] IRepository<RequestAggregate, RequestId> requests,
        [FromServices] IProjectDirectory projectDirectory,
        [FromServices] INotificationSender notifications,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        // Address by the GUID-free slug (ADR-0024): resolve to the surrogate id, then load via the keyed
        // repo. A v1-created request (null slug) never resolves here → the same 404 as a genuine miss.
        var requestId = await requestQuery.FindIdBySlugAsync(request.Slug, ct);
        if (requestId is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);          // unknown / v1 slug

        var settledRequest = await requests.FindAsync(RequestId.Create(requestId.Value), ct);
        if (settledRequest is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);          // genuine miss

        if (settledRequest.Type != RequestType.SelfOnboarding)
            return Problems.Unprocessable(RequestErrorCodes.UnsupportedType, "Only self-onboarding requests can be handled on this endpoint.");

        var shortName = await projectDirectory.FindShortNameAsync(settledRequest.ProjectId, ct);
        if (shortName is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);
        if (!caller.Scope.Contains(shortName.Value))
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.RequestSelfRejected, caller, shortName.Value) with
            {
                TargetType = AuditActions.TargetTypes.Request,
                TargetId = request.Slug,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);
        }

        var reject = settledRequest.Reject();
        if (!reject.IsSuccess)
            return reject.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        if (settledRequest.ContactEmail is { } requesterEmail)
            await notifications.SendRequestRejectedToRequesterAsync(requesterEmail, requestId.Value, shortName.Value, ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.RequestSelfRejected, caller, shortName.Value) with
        {
            TargetType = AuditActions.TargetTypes.Request,
            TargetId = request.Slug,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapRejectRequestEndpoint()
        {
            app.MapPut("Requests/{slug}/Reject", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("RejectRequestV2")
            .WithSummary("Reject a self-onboarding request")
            .WithDescription("[DEPRECATED — use Projects/{projectShortName}/OnboardingRequests] Rejects a pending self-onboarding request (terminal). Caller must hold a role on the request's project. Optional 'reason' query is accepted for parity but not persisted. Returns 204; 404 (request.not_found) when unknown or outside scope — the two are deliberately indistinguishable; 409 when already settled; 422 for non self-onboarding requests. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("Request")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}