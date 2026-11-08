namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>
/// One pre-approved entry of the ADR-0059/A1 expected-differences list. A delta that
/// <see cref="Matches"/> is classified as <see cref="DeltaClassification.ExpectedDifference"/>
/// (surfaced in the report for human confirmation) rather than an unexplained failure.
/// </summary>
/// <param name="Id">Stable slug printed in the report.</param>
/// <param name="Reason">Human explanation, straight from ADR-0059/A1.</param>
/// <param name="Matches">Predicate over a delta.</param>
public sealed record ExpectedDifference(string Id, string Reason, Func<RowDelta, bool> Matches);

/// <summary>
/// The ADR-0059/A1 pre-approved expected-differences list, encoded explicitly. Two of the five
/// A1 items — eliminated no-op writes and manual-member sweep timing — surface only through
/// audit/timestamp columns which are already dropped by <see cref="ProjectionDefaults.ExcludedFields"/>;
/// they are still listed here so the allow-list is complete and auditable, and fire if an operator
/// runs with a narrower exclusion set.
/// </summary>
public static class ExpectedDifferences
{
    /// <summary>Entities that represent a membership / permission / access grant.</summary>
    public static readonly IReadOnlySet<string> MembershipEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        EntityTypes.AssignedUserToProject,
        EntityTypes.UserPermission,
        EntityTypes.UserProjectAccess,
    };

    /// <summary>Entities written per-resource inside a project transaction.</summary>
    public static readonly IReadOnlySet<string> ResourceEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        EntityTypes.ProjectResource,
        EntityTypes.AssignedLocation,
    };

    /// <summary>Columns whose only movement is a re-written timestamp / audit user.</summary>
    public static readonly IReadOnlySet<string> AuditTimestampFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "CreatedAt",
        "ModifiedAt",
        "CreatedByUserId",
        "AssignedByUserId",
    };

    /// <summary>The full A1 list, evaluated top-to-bottom (first match wins).</summary>
    public static readonly IReadOnlyList<ExpectedDifference> All =
    [
        // A1 §5 — removed projects the OLD eligibility skip silently dropped now reach the DB as
        // IsDeleted = true (ADR-0054/A1).
        new ExpectedDifference(
            "removed-project-now-lands-as-deleted",
            "Removed project reaches the NEW DB as IsDeleted=true; the OLD eligibility skip dropped it.",
            static d => d.Kind == DeltaKind.OnlyInNew
                && d.EntityType == EntityTypes.Project
                && IsTrue(d.New?.Fields.GetValueOrDefault("IsDeleted"))),

        // A1 §4 — all-or-nothing-per-project (OLD) vs. per-resource partial progress (NEW): a
        // resource/allocation the NEW side committed while the OLD side rolled the whole project back.
        new ExpectedDifference(
            "per-resource-partial-progress",
            "NEW commits per-resource partial progress where OLD rolled the whole project back.",
            static d => d.Kind == DeltaKind.OnlyInNew && ResourceEntities.Contains(d.EntityType)),

        // A1 §1 — per-user membership (NEW) vs. all-or-nothing membership transaction (OLD): a
        // membership row the NEW side committed for one user while the OLD side failed the batch.
        // NOTE: matches on entity type alone, so it also covers a spurious extra membership from a
        // role-map bug or a lingering UserPermission (B09). CONFIRM each is a real partial-batch case.
        new ExpectedDifference(
            "per-user-vs-all-or-nothing-membership",
            "NEW commits membership per-user where OLD used one all-or-nothing transaction per project — CONFIRM this is a partial-batch artifact, not a role-map bug or lingering UserPermission (B09).",
            static d => d.Kind == DeltaKind.OnlyInNew && MembershipEntities.Contains(d.EntityType)),

        // A1 §2 — manual-member sweep timing (map-based NEW vs. DB-based OLD): a membership row the
        // OLD sweep removed at a different iteration than the NEW map-based diff.
        // WARNING: an OnlyInOld membership can equally be a GENUINE LOSS — the NEW side failed to
        // write a grant the OLD side had, so a user silently loses project access. CONFIRM before accepting.
        new ExpectedDifference(
            "manual-member-sweep-timing",
            "Membership present only in OLD (map-based vs. DB-based sweep timing) — WARNING: could be a genuine NEW-side loss where a user silently loses access; CONFIRM this is a sweep-timing artifact, not a regression.",
            static d => d.Kind == DeltaKind.OnlyInOld && MembershipEntities.Contains(d.EntityType)),

        // A1 §3 — eliminated no-op writes: only a timestamp / audit column moved. Normally absorbed
        // by the exclusion list; listed for completeness.
        new ExpectedDifference(
            "eliminated-no-op-write",
            "Only an audit/timestamp column differs; NEW eliminates the OLD no-op re-write.",
            static d => d.Kind == DeltaKind.FieldMismatch
                && d.Field is not null
                && AuditTimestampFields.Contains(d.Field)),
    ];

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || value == "1";
}