using Core.Domain.OnboardingRequest;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.OnboardingRequest.GetMyOnboardingRequests;

/// <summary>
/// Lists the onboarding requests the authenticated caller sent and the ones received on projects where the
/// caller is Owner or Manager (ADR-0107). <c>Direction</c> keeps one half, and each item says which half it
/// came from. A request in both halves is listed once, as <c>Received</c>. The received half comes from
/// <c>caller.ProjectRoles</c> alone, so a Member role or an Admin role adds nothing to it. Each item carries
/// the <see cref="Response.Slug"/> for addressing follow-up calls.
/// </summary>
internal static class GetMyOnboardingRequestsEndpoint
{
    internal sealed record Request([FromQuery] string? State, [FromQuery] string? Direction, [FromQuery] string? ProjectShortName);

    internal sealed record Response(
        string? Slug,
        string State,
        string ProjectShortName,
        string IdpUserId,
        string? ContactEmail,
        string Direction);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [AsParameters] Request request,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IOnboardingRequestQuery onboardingRequests,
        IOptions<PaginationOptions> pagination,
        IOptions<WellKnownRoles> roles,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        int? stateValue = null;
        if (request.State is { } s && !string.IsNullOrWhiteSpace(s))
        {
            if (!OnboardingRequestState.TryParseName(s, out var state))
                return Problems.Validation(OnboardingRequestErrorCodes.InvalidState, "State must be a valid onboarding request state name.");
            stateValue = state.Value;
        }

        if (ParseDirection(request.Direction, out var direction) is { } invalidDirection)
            return invalidDirection;

        if (ParseProject(request.ProjectShortName, out var project) is { } invalidProject)
            return invalidProject;

        var filter = new OnboardingRequestListFilter(
            direction is OnboardingRequestDirection.Received ? null : caller.IdpId,
            direction is OnboardingRequestDirection.Sent ? [] : ManagedProjects(caller, roles.Value),
            project,
            stateValue);

        var queryPage = await onboardingRequests.GetForCallerAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(r => new Response(
                r.Slug?.Value,
                r.State.Name,
                r.ProjectShortName.Value,
                r.IdpUserId.Value,
                r.ContactEmail.Value,
                r.Direction.ToString()))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    // why: an exact name match, because Enum.TryParse alone also accepts "sent", "1" and "Sent,Received" (D1)
    private static IResult? ParseDirection(string? value, out OnboardingRequestDirection? direction)
    {
        direction = null;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Enum.IsDefined(typeof(OnboardingRequestDirection), value))
            return Problems.Validation(OnboardingRequestErrorCodes.InvalidDirection, "Direction must be Sent or Received.");

        direction = Enum.Parse<OnboardingRequestDirection>(value);
        return null;
    }

    private static IResult? ParseProject(string? projectShortName, out ProjectShortName? project)
    {
        project = null;
        if (string.IsNullOrWhiteSpace(projectShortName))
            return null;

        if (!ProjectShortName.TryParse(projectShortName, null, out var parsed))
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "Project short name filter is invalid.");

        project = parsed;
        return null;
    }

    private static List<ProjectShortName> ManagedProjects(Caller caller, WellKnownRoles roles) =>
    [
        .. caller.ProjectRoles
            .Where(kvp => string.Equals(kvp.Key, roles.DefaultProjectOwnerRoleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kvp.Key, roles.DefaultProjectManagerRoleName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kvp => kvp.Value)
            .Distinct(),
    ];

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetMyOnboardingRequestsEndpoint()
        {
            app.MapGet("Users/Me/OnboardingRequests", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetMyOnboardingRequestsV2")
            .WithSummary("List the onboarding requests the current user sent or can act on")
            .WithDescription("Returns, as a paged list (?page=&limit=), the onboarding requests the authenticated user sent and the ones received on projects where the user is Owner or Manager. 'Direction' keeps one half: 'Sent' or 'Received', case-sensitive; without it both are returned. 'State' filters by state name and defaults to all states; send State=Pending for the requests still waiting. 'ProjectShortName' narrows both halves to one project. Each item carries the slug used to address it and a 'Direction' field ('Sent' or 'Received'); a request the user sent to a project they now manage is listed once, as 'Received', and as 'Sent' under Direction=Sent. The 'state' filter and the response 'state' field use stable PascalCase names (e.g. Pending, Accepted), not the legacy ints. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("OnboardingRequest")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
    }
}