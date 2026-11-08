namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// One failed HEAppE instance in a TemplatesInfo build, surfaced to API callers (ADR 0074).
/// <paramref name="Instance"/> is the sanitized <c>provider / resource (locations)</c> descriptor — the
/// HEAppE URL is deliberately never included. <paramref name="Cause"/> is one of the stable
/// <c>HeappeFailureCause</c> codes (<c>unauthorized</c>, <c>upstream-error</c>, <c>timeout</c>,
/// <c>invalid-response</c>, <c>unreachable</c>, <c>misconfigured</c>, <c>allocation-missing</c>, <c>internal</c>, <c>token-exchange-unavailable</c>,
/// <c>token-exchange-failed</c>); see ADR 0074 and ADR 0118.
/// </summary>
public sealed record HeappeFailureInfo(string Instance, string Cause);