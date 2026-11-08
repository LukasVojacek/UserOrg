namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>Default comparison configuration per ADR-0059/A1.</summary>
public static class ProjectionDefaults
{
    /// <summary>
    /// Fields ignored during comparison — surrogate ids (V7 GUIDs), creation/modification
    /// timestamps (<c>now()</c> fallback, <c>SyncFrom</c>-written) and audit users. If a projection
    /// accidentally includes one of these, it will not cause a spurious diff.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "Id",
        "CreatedAt",
        "ModifiedAt",
        "CreatedBy",
        "CreatedByUserId",
        "ModifiedByUserId",
        "AssignedByUserId",
    };
}