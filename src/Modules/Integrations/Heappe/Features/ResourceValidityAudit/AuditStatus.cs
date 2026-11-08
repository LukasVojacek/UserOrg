namespace Heappe.Features.ResourceValidityAudit;

/// <summary>Outcome of auditing one target. Everything other than <see cref="Ok"/> is an audit finding.</summary>
public enum AuditStatus
{
    /// <summary>HEAppE has the project record and its validity matches userorg (whole-day, UTC).</summary>
    Ok,

    /// <summary>The instance has no project record for the resource name.</summary>
    Missing,

    /// <summary>A record exists but its start/end differ from userorg's.</summary>
    ValidityMismatch,

    /// <summary>The target could not be verified — unreachable, missing credentials, or an unexpected error.</summary>
    CheckFailed,
}