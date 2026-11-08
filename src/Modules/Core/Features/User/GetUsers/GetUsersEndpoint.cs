using Core.Shared.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Core.Features.User.GetUsers;

internal static class GetUsersEndpoint
{
    internal sealed record Request(
        [FromQuery] string? ProjectShortName,
        [FromQuery] string? IdpUserId);

    internal sealed record Response(
        string IdpUserId,
        string Email,
        string UserName,
        IReadOnlyList<string> Projects,
        string? AssignedProviderName);

    internal static async Task<IResult> HandleAsync(
        HttpContext http,
        [AsParameters] Request request,
        Caller caller,
        [AsParameters] PageRequest page,
        [FromServices] IUserInfoQuery users,
        IOptions<PaginationOptions> pagination,
        CancellationToken ct)
    {
        var limit = page.ResolveLimit(pagination.Value);

        ProjectShortName? shortName = null;
        if (!string.IsNullOrWhiteSpace(request.ProjectShortName))
        {
            if (!ProjectShortName.TryParse(request.ProjectShortName, null, out var sn))
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "Invalid project short name.");
            shortName = sn;
        }

        IdpId? filterIdpId = null;
        if (!string.IsNullOrWhiteSpace(request.IdpUserId))
        {
            if (!IdpId.TryParse(request.IdpUserId, null, out var idpId))
                return Problems.Validation(CommonErrorCodes.InvalidFormat, "Invalid identity provider user ID.");
            filterIdpId = idpId;
        }

        var filter = new UserFilter(shortName, filterIdpId);
        var queryPage = await users.GetUsersAsync(filter, page.EffectivePage, limit, ct);

        var response = queryPage.Items
            .Select(u => new Response(
                u.IdentityProviderId.Value,
                u.Email.Value,
                u.UserName.Value,
                [.. u.Projects.Select(p => p.Value)],
                u.AssignedProviderName?.Value))
            .ToList();

        return PagedResponse.Offset(http, response, page.EffectivePage, limit, queryPage.TotalItems);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetUsersEndpoint()
        {
            app.MapGet("Users", HandleAsync)
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.Admin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetUsersV2")
            .WithSummary("List users")
            .WithDescription("Returns users, optionally filtered by project short name or identity provider user ID.")
            .WithTags("User")
            .Produces<IReadOnlyList<Response>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}