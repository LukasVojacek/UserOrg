namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>
/// One entity projected to its natural key plus the comparable field values. Surrogate ids,
/// <c>CreatedAt</c>/<c>ModifiedAt</c> and audit-user columns are dropped by the projection (or
/// ignored via the exclusion list) so V7-GUID ids and <c>now()</c> timestamps never cause a diff.
/// FKs are joined through to external ids / names, so the same logical row projects identically
/// from the OLD and the NEW UserOrg database.
/// </summary>
/// <param name="EntityType">Logical table, e.g. <c>Project</c>, <c>ProjectResource</c>.</param>
/// <param name="Key">Natural key, e.g. <c>externalId|serviceName</c>.</param>
/// <param name="Fields">Comparable field values keyed by column name.</param>
public sealed record NaturalKeyRow(
    string EntityType,
    string Key,
    IReadOnlyDictionary<string, string?> Fields)
{
    /// <summary>Stable cross-database identity used to pair OLD and NEW rows.</summary>
    public string Identity => $"{EntityType}|{Key}";
}