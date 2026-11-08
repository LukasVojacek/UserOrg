using Lukdrasil.Error;

using Microsoft.AspNetCore.Http;

namespace Heappe.Features.KillSwitch;

/// <summary>
/// Error codes stamped on <see cref="Lukdrasil.Error.Error.Code"/> by the KillSwitch feature, so
/// endpoints can special-case an HTTP status the shared <c>Result.State</c> enum has no member for
/// (409 Conflict), without re-deriving the kill-switch rule at the endpoint layer.
/// </summary>
internal static class KillSwitchErrorCodes
{
    /// <summary>The write/sync guards' target Location is frozen by an enforced kill-switch.</summary>
    internal const string LocationFrozen = "LOCATION_FROZEN";

    /// <summary>
    /// The 409 response for a frozen-location rejection, or <see langword="null"/> when
    /// <paramref name="error"/> is any other failure (which each write path maps its own way).
    /// Every write path needs this because the shared <c>Result.State</c> enum has no Conflict
    /// member, so <c>State.ToHttpStatusCode()</c> can never produce 409 (ADR-0077 §5).
    /// </summary>
    internal static IResult? FrozenConflictOrNull(Error error)
        => error.Code == LocationFrozen
            ? TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: error.Description)
            : null;
}

internal static class Errors
{
    /// <summary>
    /// Builds the write-guard rejection error (ADR-0077 §5). <c>reason</c> is deliberately excluded —
    /// it is admin-only and may describe an unpatched vulnerability.
    /// </summary>
    internal static Error LocationFrozenByKillSwitch(string locationName, DateTime enforcedAtUtc) => new ErrorBuilder()
        .WithCode(KillSwitchErrorCodes.LocationFrozen)
        .WithSeverity(ErrorSeverity.Warning)
        .WithDescription("Command templates on location '{0}' are frozen by an active kill-switch (since {1})")
        .Build(locationName, enforcedAtUtc);
}