using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Request.GetSentRequests;

internal static class GetSentRequestsEndpoint
{
    internal sealed record Request(
        [FromQuery] string? State);

    internal sealed record Response(
        string? ContactEmail,
        string Type,
        string State,
        string ProjectShortName,
        string IdpUserId,
        string? ProviderName,
        string? ProjectResourceName);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [AsParameters] Request request,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IRequestQuery requests,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        int? stateValue = null;
        if (request.State is { } s && !string.IsNullOrWhiteSpace(s))
        {
            if (!RequestState.TryParseName(s, out var state))
                return Problems.Validation(RequestErrorCodes.InvalidState, "State must be a valid request state name.");
            stateValue = state.Value;
        }

        var filter = new RequestFilter(null, null, stateValue, SenderIdpId: caller.IdpId);
        var queryPage = await requests.GetRequestsAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(r => new Response(
                r.ContactEmail?.Value,
                r.Type.Name,
                r.State.Name,
                r.ProjectShortName.Value,
                r.IdpUserId.Value,
                r.ProviderName?.Value,
                r.ProjectResourceName?.Value))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetSentRequestsEndpoint()
        {
            app.MapGet("Requests/Sent", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetSentRequestsV2")
            .WithSummary("List requests sent by the current user")
            .WithDescription("[DEPRECATED — use Users/Me/OnboardingRequests?Direction=Sent] Returns requests submitted by the authenticated user, optionally filtered by state. The 'state' filter and the response 'type'/'state' fields use stable PascalCase names, not the legacy ints. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("Request")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
    }
}