namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>Shape of a single difference between the OLD and NEW projections.</summary>
public enum DeltaKind
{
    /// <summary>Row present only in the OLD database.</summary>
    OnlyInOld,

    /// <summary>Row present only in the NEW database.</summary>
    OnlyInNew,

    /// <summary>Row present in both, one non-excluded field differs.</summary>
    FieldMismatch,

    /// <summary>The same natural key projects to more than one row on a side — always investigate.</summary>
    DuplicateIdentity,
}

/// <summary>
/// A single natural-key-level difference. For <see cref="DeltaKind.FieldMismatch"/> the
/// <see cref="Field"/>/<see cref="OldValue"/>/<see cref="NewValue"/> members are populated.
/// </summary>
public sealed record RowDelta(
    string EntityType,
    string Key,
    DeltaKind Kind,
    NaturalKeyRow? Old,
    NaturalKeyRow? New,
    string? Field = null,
    string? OldValue = null,
    string? NewValue = null);