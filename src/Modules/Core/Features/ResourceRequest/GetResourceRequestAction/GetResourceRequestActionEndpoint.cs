using Core.Features.ResourceRequest.PublicAction;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Core.Features.ResourceRequest.GetResourceRequestAction;

/// <summary>
/// Anonymous action-link preview: given the single-use token emailed to the PI, returns the request's detail so
/// the approval page can render provider/resource/requester before the PI decides (ADR-0064). Possession of the
/// live token is the authority — no authentication. An unknown, expired, or already-used token yields the uniform
/// 404 (<see cref="ResourceRequestActionProblems.ActionNotFound"/>) so token validity never leaks.
/// </summary>
internal static class GetResourceRequestActionEndpoint
{
    internal sealed record Request([FromRoute] string Token);

    internal sealed record Response(
        string Slug,
        string ProjectShortName,
        string ProviderName,
        string ResourceName,
        string RequesterIdpId,
        string State);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IProjectResourceRequestQuery query,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        CancellationToken ct)
    {
        var detail = await query.FindByActionTokenAsync(request.Token, ct);
        if (detail is null)
        {
            await auditLogger.AuditAsync(ActionLinkAudit.Denied(
                AuditActions.ResourceRequestActionViewed, request.Token, ResourceRequestActionProblems.ActionNotFoundCode));
            return ResourceRequestActionProblems.ActionNotFound();
        }

        await auditLogger.AuditAsync(ActionLinkAudit.Success(
            AuditActions.ResourceRequestActionViewed, request.Token, detail.ProjectShortName,
            new Dictionary<string, object?> { ["slug"] = detail.Slug, ["requester_idp_id"] = detail.RequesterIdpId }));

        return Results.Ok(new Response(
            detail.Slug,
            detail.ProjectShortName,
            detail.ProviderName,
            detail.ResourceName,
            detail.RequesterIdpId,
            detail.State));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetResourceRequestActionEndpoint()
        {
            app.MapGet("Public/ResourceRequestActions/{token}", HandleAsync)
                .AllowAnonymous()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetResourceRequestActionV2")
            .WithSummary("Preview a resource-request action link")
            .WithDescription("Returns the resource request behind a single-use PI action-link token. No authentication required. Any unknown, expired, or already-used token returns an identical 404 (code resource_request.action_not_found) so token validity never leaks. Read the machine-readable code; do not parse the message.")
            .WithTags("Public")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}