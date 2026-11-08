using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.Request.GetPublicRequestInfo;

internal static class GetPublicRequestInfoEndpoint
{
    internal sealed record Request([FromRoute] string ActionLink);

    internal sealed record Response(
        string? RequestorUserName,
        string? RequestorEmail,
        string ProjectShortName,
        string? ProjectResourceName,
        string? ProviderName,
        string RequestState,
        DateTimeOffset ValidTo,
        string RequestType,
        string? AcceptActionLink,
        string? RejectActionLink);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IRequestQuery requests,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        // Addressed by the single-use action-link token, not a surrogate id (ADR-0024). The anonymous
        // approver holds the link from their email; the token (a 122-bit GUID) keeps this token-dispensing
        // endpoint unguessable, which a low-entropy public slug would not. Either sibling token resolves
        // the request; once handled, the links are soft-deleted so the token no longer resolves → not found.
        var target = await requests.FindActionLinkTargetAsync(request.ActionLink, ct);
        if (target is null)
            return await DeniedAsync(RequestErrorCodes.NotFound, "Request not found.");

        var summary = await requests.GetPublicRequestSummaryAsync(target.RequestId, ct);

        if (summary is null)
            return await DeniedAsync(RequestErrorCodes.NotFound, "Request not found.");

        if (summary.RequestState > RequestState.PendingForHPCProject.Value)
            return await DeniedAsync(RequestErrorCodes.NotPending, "Request is no longer pending.");

        if (summary.ValidTo < DateTimeOffset.UtcNow)
            return await DeniedAsync(RequestErrorCodes.Expired, "Request action link has expired.");

        if (summary.RequestType != RequestType.Create(2).Value)
            return await DeniedAsync(RequestErrorCodes.InvalidType, "Request type is not valid for this action link.");

        await auditLogger.AuditAsync(ActionLinkAudit.Success(
            AuditActions.RequestActionLinkViewed, request.ActionLink, summary.ProjectShortName.Value) with
        {
            TargetType = AuditActions.TargetTypes.Request,
            TargetId = summary.Slug ?? summary.RequestId.ToString(),
        });

        return Results.Ok(new Response(
            summary.RequestorUserName,
            summary.RequestorEmail,
            summary.ProjectShortName.Value,
            summary.ProjectResourceName,
            summary.ProviderName,
            RequestState.Create(summary.RequestState).Name,
            summary.ValidTo,
            RequestType.Create(summary.RequestType).Name,
            summary.AcceptActionLink,
            summary.RejectActionLink));

        async Task<IResult> DeniedAsync(string code, string detail)
        {
            await auditLogger.AuditAsync(ActionLinkAudit.Denied(AuditActions.RequestActionLinkViewed, request.ActionLink, code));
            return Problems.Validation(code, detail);
        }
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetPublicRequestInfoEndpoint()
        {
            app.MapGet("Public/Requests/ByLink/{actionLink}", HandleAsync)
                .AllowAnonymous()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetPublicRequestInfoV2")
            .WithSummary("Get public request info for action link")
            .WithDescription("Returns request details for an action link. No authentication required. On a failure read the 'code' extension for a machine-readable code (request.not_found, request.not_pending, request.expired, request.invalid_type); do not parse the message.")
            .WithTags("Public")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
    }
}