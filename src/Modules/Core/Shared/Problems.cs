using System.Diagnostics;

using Microsoft.AspNetCore.Http;

namespace Core.Shared;

/// <summary>
/// Builds the uniform v2 error response shape (RFC 9457 <c>ProblemDetails</c>) with a stable, machine-readable
/// <c>code</c> in its extensions (ADR-0020). This helper owns the *shape*; the calling endpoint owns the
/// concrete <paramref name="code"/>/detail it emits — there is no central mandatory error enum. The
/// <c>aggregate.snake_case</c> code convention matches the domain error codes surfaced by
/// <see cref="DomainErrorResults"/>.
/// </summary>
internal static class Problems
{
    /// <summary>
    /// Every helper stamps <c>extensions["requestId"]</c> from the ambient <see cref="Activity"/> here, since
    /// none of these helpers receive <c>HttpContext</c> and this <c>IResult</c> path bypasses
    /// <c>IProblemDetailsService</c> (ADR-0020), where the equivalent stamp normally happens.
    /// </summary>
    private static object? RequestId => Activity.Current?.Id;

    private static IResult Of(int status, string code, string? detail) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["requestId"] = RequestId });

    /// <summary>
    /// 404. The same <paramref name="code"/>/<paramref name="detail"/> MUST be used for a genuinely-missing
    /// resource and for one hidden because it is outside the caller's read scope, so existence does not leak
    /// (ADR-0018 rider). Pass an aggregate code such as <c>project.not_found</c>.
    /// </summary>
    internal static IResult NotFound(string code, string? detail = null) =>
        Of(StatusCodes.Status404NotFound, code, detail);

    internal static IResult ProjectNotFound() =>
        NotFound("project.not_found", "Project not found.");

    internal static IResult CallerNotProvisioned() =>
        NotFound(CommonErrorCodes.CallerNotProvisioned, "The authenticated caller is not a known user.");

    internal static IResult RoleServiceUnavailable() =>
        Upstream(CommonErrorCodes.RoleServiceUnavailable, "Failed to retrieve roles.");

    internal static IResult ProviderWriteForbidden() =>
        Forbidden("provider.write_forbidden", "This identity may not write to this provider.");

    internal static IResult CallerDeactivated() =>
        Forbidden(UserErrorCodes.Deactivated, "The authenticated caller has been deactivated.");

    internal static IResult Conflict(string code, string detail) =>
        Of(StatusCodes.Status409Conflict, code, detail);

    internal static IResult Forbidden(string code, string? detail = null) =>
        Of(StatusCodes.Status403Forbidden, code, detail);

    internal static IResult Unprocessable(string code, string detail) =>
        Of(StatusCodes.Status422UnprocessableEntity, code, detail);

    internal static IResult Upstream(string code, string detail) =>
        Of(StatusCodes.Status502BadGateway, code, detail);

    /// <summary>
    /// 502 for several unreachable upstream instances at once: one human <paramref name="detail"/> summary and
    /// <c>extensions["unreachableLocations"] = [{ location, endpoint }]</c> listing every one that failed its
    /// liveness probe — not just the first — so the caller doesn't have to retry the whole batch N times against
    /// a live external dependency to find out which instance(s) are down (ADR-0050 §4).
    /// </summary>
    internal static IResult Upstream(string code, string detail, IReadOnlyCollection<(string LocationName, string Endpoint)> unreachable) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["unreachableLocations"] = unreachable.Select(l => new { location = l.LocationName, endpoint = l.Endpoint }).ToArray(),
                ["requestId"] = RequestId,
            });

    /// <summary>
    /// 400 for a single invalid input: one human <paramref name="message"/> in <c>detail</c>, the machine
    /// error type in <c>extensions["code"]</c> (ADR-0020). The code is specific (e.g. <c>request.invalid_type</c>)
    /// so it identifies both the failure and the field without per-field message duplication.
    /// </summary>
    internal static IResult Validation(string code, string message) =>
        Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed",
            extensions: new Dictionary<string, object?> { ["code"] = code, ["requestId"] = RequestId });

    /// <summary>
    /// 400 for several invalid inputs: one human <paramref name="detail"/> summary, top-level
    /// <c>code = validation.failed</c>, and <c>extensions["fieldErrors"] = [{ field, code }]</c> — codes only,
    /// no per-field messages (ADR-0020).
    /// </summary>
    internal static IResult Validation(string detail, IReadOnlyCollection<(string Field, string Code)> fieldErrors) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = CommonErrorCodes.ValidationFailed,
                ["fieldErrors"] = fieldErrors.Select(f => new { field = f.Field, code = f.Code }).ToArray(),
                ["requestId"] = RequestId,
            });
}