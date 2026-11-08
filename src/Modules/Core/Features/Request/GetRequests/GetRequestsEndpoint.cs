using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.Request.GetRequests;

internal static class GetRequestsEndpoint
{
    internal sealed record Request(
        [FromQuery] string? ProjectShortName,
        [FromQuery] string? IdpUserId,
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
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        if (ParseState(request.State, out var stateValue) is { } invalidState)
            return invalidState;

        if (ParseProject(request.ProjectShortName, out var projectFilter) is { } invalidProject)
            return invalidProject;

        IdpId? userFilter = request.IdpUserId is { } iu && !string.IsNullOrWhiteSpace(iu) ? new IdpId(iu) : null;

        // Admin sees every request; everyone else sees only requests on projects in their caller scope.
        // No provider tier: RBAC does not grant a provider identity access to v2/requests (ADR-0018 / F0).
        IReadOnlyList<ProjectShortName>? scope = caller.IsAdmin(roleOptions.Value) ? null : [.. caller.Scope];

        var filter = new RequestFilter(projectFilter, userFilter, stateValue, scope);

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

    private static IResult? ParseState(string? state, out int? stateValue)
    {
        stateValue = null;
        if (string.IsNullOrWhiteSpace(state))
            return null;

        if (!RequestState.TryParseName(state, out var parsed))
            return Problems.Validation(RequestErrorCodes.InvalidState, "State must be a valid request state name.");

        stateValue = parsed.Value;
        return null;
    }

    private static IResult? ParseProject(string? projectShortName, out ProjectShortName? projectFilter)
    {
        projectFilter = null;
        if (string.IsNullOrWhiteSpace(projectShortName))
            return null;

        if (!ProjectShortName.TryParse(projectShortName, null, out var parsed))
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Project short name filter is invalid.");

        projectFilter = parsed;
        return null;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetRequestsEndpoint()
        {
            app.MapGet("Requests", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetRequestsV2")
            .WithSummary("List requests")
            .WithDescription("[DEPRECATED — use Users/Me/OnboardingRequests?Direction=Received] Returns requests on projects the authenticated user has a role on, optionally filtered by project, user, and state. The 'state' filter and the response 'type'/'state' fields use stable PascalCase names (e.g. Pending, SelfOnboarding), not the legacy ints. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("Request")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);
    }
}