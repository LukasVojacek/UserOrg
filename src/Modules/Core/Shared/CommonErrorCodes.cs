namespace Core.Shared;

/// <summary>
/// Cross-cutting, non-aggregate-specific error codes for the v2 contract (ADR-0020). Aggregate-specific
/// codes live in the domain's <c>XxxErrorCodes</c> classes; these cover failures common to many endpoints
/// so the codes are not hand-typed as literals at each call site.
/// </summary>
internal static class CommonErrorCodes
{
    internal const string ValidationFailed = "validation.failed";
    internal const string Required = "validation.required";
    internal const string InvalidFormat = "validation.invalid_format";
    internal const string RemovedParameter = "validation.removed_parameter";

    internal const string CallerNotProvisioned = "caller.not_provisioned";

    internal const string RoleServiceUnavailable = "upstream.role_service_unavailable";
}