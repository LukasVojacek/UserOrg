using Heappe.Features.KillSwitch.Contract;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;

using UserOrg.Shared;

namespace Heappe.Features.KillSwitch;

/// <summary>
/// Provides the admin endpoints for freezing HEAppE command templates on a Location or across a whole provider.
/// </summary>
/// <remarks>These routes are the only surface exposing a kill-switch's <c>reason</c> and <c>enforcedAt</c>; they
/// require <c>perm:provider:admin</c> or <c>perm:platform:admin</c> and are mapped in <c>roleManagmentRBAC.json</c>
/// — an unmapped route is fail-closed 403 at the Gateway.</remarks>
public static class KillSwitchEndpoints
{
    /// <summary>
    /// Adds the five kill-switch admin routes under <c>/KillSwitch</c> to the route builder.
    /// </summary>
    /// <param name="app">The endpoint route builder to which the kill-switch endpoints will be added.</param>
    /// <returns>The same <see cref="IEndpointRouteBuilder"/> instance, enabling further configuration of endpoints.</returns>
    public static IEndpointRouteBuilder MapKillSwitchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/KillSwitch", async (
            [FromServices] KillSwitchAdminHandler handler,
            [FromServices] IHttpContextAccessor httpContextAccessor,
            CancellationToken ct = default) =>
            await WithCallerAsync(httpContextAccessor, caller => handler.ListAsync(caller, ct)))
            .MapDocumentation<IReadOnlyList<KillSwitchLocationState>>(
                "GetKillSwitches",
                "List the locations you may manage and their kill-switch state",
                "Lists every location whose kill-switch the caller may manage — all locations for a platform admin, only the locations of the caller's own provider for a provider admin — each with its current kill-switch state: whether it is enforced, the reason and UTC timestamp of the last enforcement, and whether the current freeze ignores that location's whitelist. A location that has never been frozen is reported as not enforced with no reason. Answers 403 when the caller is neither a platform admin nor associated with a provider. This route and the per-location GET are the only places the freeze reason and timestamp are exposed.");

        app.MapGet("/KillSwitch/{ProviderName}/{LocationName}", async (
            [FromRoute(Name = "ProviderName")] string providerName,
            [FromRoute(Name = "LocationName")] string locationName,
            [FromServices] KillSwitchAdminHandler handler,
            [FromServices] IHttpContextAccessor httpContextAccessor,
            CancellationToken ct = default) =>
            await WithCallerAsync(httpContextAccessor, caller => handler.GetLocationAsync(caller, providerName, locationName, ct)))
            .MapDocumentation<LocationKillSwitchDetail>(
                "GetKillSwitch",
                "Inspect one location's kill-switch and the effective state of its command templates",
                "Reports one location's kill-switch state together with every HEAppE command template that has a partition on that location. Each template carries its stored state, which a kill-switch never rewrites, the effective state readers see (Frozen while the kill-switch kills it here), and whether it is on the location's whitelist — a template can be whitelisted and still read Frozen while the freeze is under lockdown. Answers 403 when the location belongs to another provider, and 409 when the provider or location name is unknown or matches more than one.")
            .DescribeLocationRouteParameters();

        app.MapPut("/KillSwitch/{ProviderName}/{LocationName}", async (
            [FromRoute(Name = "ProviderName")] string providerName,
            [FromRoute(Name = "LocationName")] string locationName,
            [FromBody] SetLocationKillSwitchRequest request,
            [FromServices] KillSwitchAdminHandler handler,
            [FromServices] IHttpContextAccessor httpContextAccessor,
            CancellationToken ct = default) =>
            await WithCallerAsync(httpContextAccessor, caller => handler.SetLocationAsync(caller, providerName, locationName, request, ct)))
            .Accepts<SetLocationKillSwitchRequest>("application/json")
            .MapDocumentation<KillSwitchLocationUpdateResult>(
                "SetKillSwitch",
                "Freeze or unfreeze all HEAppE command templates on one location",
                "Enforces or lifts the kill-switch on a single location. Enforcing (`isEnforced: true`) requires a reason of at least 10 characters, which is stored with the acting user and the UTC time, and makes every HEAppE command template with a partition on the location read as Frozen on every read path while Activate/Deactivate against it is refused with 409; templates on the location's standing whitelist are exempt unless `ignoreWhitelist` is set, which suppresses the whitelist for the duration of the freeze without deleting a single row. Lifting clears the freeze, always resets `ignoreWhitelist`, leaves the whitelist standing for the next incident, and queues a command template resync for every (project, resource) on the location — those the bounded resync queue could not accept are returned in `unqueuedResyncItems` so the administrator can re-run them. The whitelist itself is never written here; use the Whitelist route. Answers 400 on a missing or too short reason and on `ignoreWhitelist` without a freeze, 403 for another provider's location, and 409 when a name is unknown or matches more than one.")
            .DescribeLocationRouteParameters();

        app.MapPut("/KillSwitch/{ProviderName}/{LocationName}/Whitelist", async (
            [FromRoute(Name = "ProviderName")] string providerName,
            [FromRoute(Name = "LocationName")] string locationName,
            [FromBody] SetLocationWhitelistRequest request,
            [FromServices] KillSwitchAdminHandler handler,
            [FromServices] IHttpContextAccessor httpContextAccessor,
            CancellationToken ct = default) =>
            await WithCallerAsync(httpContextAccessor, caller => handler.SetLocationWhitelistAsync(caller, providerName, locationName, request, ct)))
            .Accepts<SetLocationWhitelistRequest>("application/json")
            .MapDocumentation<LocationKillSwitchDetail>(
                "SetKillSwitchWhitelist",
                "Replace the standing list of command templates that keep running while one location is frozen",
                "REPLACES the location's standing whitelist — the command templates that keep running while the location is frozen — with the posted list, stamping who added each exception and when. Replace, not add or remove: posting an empty list clears the whitelist. Every item is addressed by its (project short name, resource name, template name) triple and must resolve to a template on THIS location, or the whole request is refused with 400 before a single row is written, so a typo can never leave a template silently frozen or silently exempt. Writing the whitelist is independent of the kill-switch state — it is configuration prepared ahead of an incident, so it works whether or not the location is currently frozen, and it neither enforces nor lifts anything. Returns the location's full kill-switch detail as it stands afterwards. Answers 403 for another provider's location and 409 when a name is unknown or matches more than one.")
            .DescribeLocationRouteParameters();

        app.MapPut("/KillSwitch/{ProviderName}", async (
            [FromRoute(Name = "ProviderName")] string providerName,
            [FromBody] SetProviderKillSwitchRequest request,
            [FromServices] KillSwitchAdminHandler handler,
            [FromServices] IHttpContextAccessor httpContextAccessor,
            CancellationToken ct = default) =>
            await WithCallerAsync(httpContextAccessor, caller => handler.SetProviderAsync(caller, providerName, request, ct)))
            .Accepts<SetProviderKillSwitchRequest>("application/json")
            .MapDocumentation<KillSwitchProviderUpdateResult>(
                "SetProviderKillSwitch",
                "Freeze or unfreeze every current location of a provider in one action",
                "Enforces or lifts the kill-switch across ALL of the provider's locations in one action — the fastest incident response, with the same per-location semantics fanned out. The covered set is a snapshot taken now: a location added to the provider afterwards is not covered, and the action has to be re-run to include it. Enforcing requires a reason of at least 10 characters and never deletes a whitelist; `ignoreWhitelist` is how you freeze everything including the exceptions, and it is fanned out to every location. Lifting clears the freeze and the lockdown flag everywhere, leaves every whitelist standing, and queues a command template resync for the affected (project, resource) pairs — those the bounded queue could not accept come back in `unqueuedResyncItems`. The response reports how many locations were covered and how many whitelist exceptions remain in force across them as a COUNT, not a list, so this path never pays for name resolution. This route carries no whitelist of its own. Answers 400 on a missing or too short reason and on `ignoreWhitelist` without a freeze, 403 for another provider, and 409 when the provider name is unknown or matches more than one.")
            .DescribeLocationRouteParameters();

        return app;
    }

    /// <summary>
    /// Reads the caller from claims at the endpoint boundary and hands it to the handler, which authorizes
    /// (ADR-0080): the module's endpoints already read claims this way, and it keeps HTTP out of the handler.
    /// </summary>
    private static async Task<IResult> WithCallerAsync(IHttpContextAccessor httpContextAccessor, Func<CallerInfo, Task<IResult>> handle)
    {
        var httpContext = httpContextAccessor.HttpContext;
        return httpContext is null
            ? Results.Problem("HttpContext is not available.", statusCode: StatusCodes.Status500InternalServerError)
            : await handle(httpContext.GetCallerInfo());
    }

    /// <summary>
    /// Describes the route parameters the kill-switch routes share, looked up BY NAME rather than by position:
    /// the routes carry different parameter sets (the provider-wide PUT has no <c>LocationName</c>), and an
    /// index that is right on one of them is out of range on another.
    /// </summary>
    private static RouteHandlerBuilder DescribeLocationRouteParameters(this RouteHandlerBuilder builder)
        => builder.WithOpenApiDoc(operation =>
        {
            Describe(operation, "ProviderName", "Name of the LEXIS Provider that owns the location. Must match exactly one provider.");
            Describe(operation, "LocationName", "Name of the provider's location, as listed by GET /KillSwitch. Must match exactly one location of that provider.");
            return operation;
        });

    /// <summary>
    /// Sets a parameter's description when the operation actually has a parameter of that name, and does
    /// nothing when it does not.
    /// </summary>
    private static void Describe(OpenApiOperation operation, string name, string description)
    {
        var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == name);
        if (parameter is not null)
        {
            parameter.Description = description;
        }
    }

    private static RouteHandlerBuilder MapDocumentation<T>(this RouteHandlerBuilder builder, string name, string summary, string description)
        => builder
            .WithName(name)
            .WithSummary(summary)
            .WithDescription(description)
            .WithTags("KillSwitch")
            .Produces<T>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
}