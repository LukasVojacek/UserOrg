namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// The outcome of auditing one target. Dates are compared and reported at whole-day UTC granularity.
/// Actual dates are null when no record was found; all dates are null and <see cref="Reason"/> is set for
/// <see cref="AuditStatus.CheckFailed"/>.
/// </summary>
public sealed record TargetAuditResult(
    string ResourceName,
    string ProjectShortName,
    Uri HeappeUri,
    AuditStatus Status,
    DateOnly? ExpectedStart,
    DateOnly? ExpectedEnd,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    string? Reason)
{
    /// <summary>True for any non-<see cref="AuditStatus.Ok"/> outcome — i.e. anything that belongs in a notification.</summary>
    public bool IsFinding => Status is not AuditStatus.Ok;
}