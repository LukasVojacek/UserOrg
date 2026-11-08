using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Core.Features.User.GetUserPreferences;

internal static class GetUserPreferencesEndpoint
{
    internal sealed record Response(JsonElement Preferences);

    internal static async Task<IResult> HandleAsync(
        Caller caller,
        [FromServices] IUserInfoQuery users,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var stored = await users.GetUserPreferencesAsync(caller.IdpId, ct);

        JsonElement preferences;
        try
        {
            using var document = JsonDocument.Parse(stored);
            preferences = document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            // Historical rows only — both the v2 query and legacy service coalesce a missing row to "{}",
            // so a live write path never produces invalid JSON here.
            var logger = loggerFactory.CreateLogger(typeof(GetUserPreferencesEndpoint).FullName!);
            logger.LogWarning(
                e, "Stored preferences for caller {IdpId} are not valid JSON — returning empty object", caller.IdpId.Value);

            using var empty = JsonDocument.Parse("{}");
            preferences = empty.RootElement.Clone();
        }

        return Results.Ok(new Response(preferences));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetUserPreferencesEndpoint()
        {
            app.MapGet("Users/Me/Preferences", HandleAsync)
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetUserPreferencesV2")
            .WithSummary("Get current user preferences")
            .WithDescription("""Returns the authenticated user's JSON preferences, wrapped in a {"Preferences": {…}} envelope.""")
            .WithTags("User")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}