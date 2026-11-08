using Core.DataAccess.ResourceRequests;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.ResourceRequest.GetProjectResourceRequests;

/// <summary>
/// Lists the resource-assignment requests raised on a project (ADR-0063, route 2). Gated by
/// <see cref="CallerScope.RequireProjectScope"/>: a caller without a role on the route project gets the standard
/// project not_found (ADR-0018), so the list leaks nothing. Optionally filtered by <c>?state=</c> (a stable
/// PascalCase <see cref="ResourceRequestState"/> name) and paged via <c>?page=&amp;limit=</c>. Each item carries
/// the slug used to address the subsequent cancel/resend/token calls.
/// </summary>
internal static class GetProjectResourceRequestsEndpoint
{
    internal sealed record Request([FromQuery] string? State);

    internal sealed record Response(
        string Slug,
        string State,
        string ProviderName,
        string ResourceName,
        string RequesterIdpId,
        DateTimeOffset CreatedAt);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [FromRoute] ProjectShortName projectShortName,
        [AsParameters] Request request,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IProjectResourceRequestQuery requests,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(projectShortName) is { } denied)
            return denied;

        var limit = page.ResolveLimit(pagination.Value);

        string? stateValue = null;
        if (request.State is { } s && !string.IsNullOrWhiteSpace(s))
        {
            if (!ResourceRequestState.TryFrom(s, out var state))
                return Problems.Validation("resource_request.invalid_state", "State must be a valid resource-request state name.");
            stateValue = state.Value;
        }

        var queryPage = await requests.ListByProjectAsync(projectShortName.Value, stateValue, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(r => new Response(
                r.Slug,
                r.State,
                r.ProviderName,
                r.ResourceName,
                r.RequesterIdpId,
                r.CreatedAt))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectResourceRequestsEndpoint()
        {
            app.MapGet("Projects/{projectShortName}/ResourceRequests", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetProjectResourceRequestsV2")
            .WithSummary("List resource-assignment requests on a project")
            .WithDescription("Returns the resource-assignment requests raised on the specified project, optionally filtered by state, as a paged list (?page=&limit=). The caller must hold a role on the project (out-of-scope is reported as 404 project.not_found, ADR-0018). Each item carries the slug used to address the request's subsequent actions. The 'state' filter and the response 'state' field use stable PascalCase names (e.g. PendingPiApproval, Assigned). Status: 200 OK, 400 Bad Request (invalid state), 401 Unauthorized. On failure, read the 'code' extension, not the message.")
            .WithTags("ResourceRequest")
            .WithGroupName(Core.Composition.ApiAudience.User)
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
    }
}