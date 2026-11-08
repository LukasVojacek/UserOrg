using Core.Domain.OnboardingRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Extensions;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;

namespace Core.Features.OnboardingRequest.RejectOnboardingRequest;

/// <summary>
/// Rejects a pending onboarding request (terminal). The onboarding twin of <c>PUT Requests/{slug}/Reject</c>.
/// Project scope is enforced on the route project (ADR-0048): the caller must hold a role on it. Because both
/// the project and the slug are in the path, the handler also asserts the resolved request belongs to the
/// route project; a genuine miss, an out-of-scope caller, and a wrong-project slug all return the
/// byte-identical <c>onboarding_request.not_found</c> problem so existence never leaks (ADR-0018 rider).
/// </summary>
internal static class RejectOnboardingRequestEndpoint
{
    internal sealed record Request([FromRoute] string ProjectShortName, [FromRoute] string Slug);

    // A genuine miss, an out-of-scope caller, and a wrong-project slug MUST return the identical problem so
    // existence never leaks (ADR-0018 rider; ADR-0048 wrong-project rider). All paths use this code + detail.
    private const string NotFoundCode = OnboardingRequestErrorCodes.NotFound;
    private const string NotFoundDetail = "Request not found.";

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IOnboardingRequestQuery onboardingRequestQuery,
        [FromServices] IRepository<OnboardingRequestAggregate, OnboardingRequestSlug> onboardingRequests,
        [FromServices] INotificationSender notifications,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        if (!ProjectShortName.TryParse(request.ProjectShortName, null, out var routeShortName))
            return Problems.NotFound(NotFoundCode, NotFoundDetail);

        if (await OnboardingSettlementLookup.FindSettleableAsync(
                request.Slug, routeShortName, caller, AuditActions.OnboardingRequestSelfRejected,
                onboardingRequestQuery, onboardingRequests, auditLogger, ct) is not { } found)
            return Problems.NotFound(NotFoundCode, NotFoundDetail);

        var (settlement, settledRequest) = found;

        var reject = settledRequest.Reject();
        if (!reject.IsSuccess)
            return reject.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        // Read back the surrogate id (db-generated; not on the aggregate) for the notification only.
        var requestId = await onboardingRequestQuery.FindIdBySlugAsync(request.Slug, ct) ?? Guid.Empty;
        await notifications.SendRequestRejectedToRequesterAsync(settledRequest.ContactEmail, requestId, routeShortName, ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.OnboardingRequestSelfRejected, caller, routeShortName,
            new Dictionary<string, object?> { ["requester_idp_id"] = settlement.Requester.Value }) with
        {
            TargetType = AuditActions.TargetTypes.OnboardingRequest,
            TargetId = request.Slug,
        });

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapRejectOnboardingRequestEndpoint()
        {
            app.MapPut("Projects/{projectShortName}/OnboardingRequests/{slug}/Reject", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("RejectOnboardingRequestV2")
            .WithSummary("Reject an onboarding request")
            .WithDescription("Rejects a pending onboarding request (terminal). Caller must hold a role on the route project. Returns 204; 404 (onboarding_request.not_found) when the request is unknown, outside the caller's scope, or belongs to a different project — the three are deliberately indistinguishable; 409 when the request is already settled. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("OnboardingRequest")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}