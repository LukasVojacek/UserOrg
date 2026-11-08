namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>One candidate row for the ADR-0059/A3 non-empty-DB check.</summary>
/// <param name="Table">Physical table the row came from.</param>
/// <param name="ServiceName">The <c>ServiceName</c> the row actually carries.</param>
/// <param name="OwnerId">
/// The row's external owner id (project/provider/resource external id) used for instance scoping, or
/// <c>null</c> when the table has no such id. Only meaningful for the attributed count.
/// </param>
public sealed record ServiceNameRow(string Table, string ServiceName, string? OwnerId = null);

/// <summary>
/// ADR-0059/A3 non-empty-database check. Existing production rows written by the buggy Puhuri path
/// carry the EFP <c>ServiceName</c> — so <c>ServiceName</c> alone cannot identify which rows belong to
/// Puhuri, and the tool has no independent ownership signal. This class therefore does NOT claim a
/// "mismatch count" it cannot soundly compute:
/// <list type="bullet">
///   <item><see cref="Histogram"/> — the honest default: rows per distinct <c>ServiceName</c> per
///   table. A human spots buggy EFP-named Puhuri rows using their own knowledge.</item>
///   <item><see cref="CountOwnedMismatches"/> — the sound A3 signal, available only when the operator
///   supplies the set of external ids known to belong to the instance under audit (from the Waldur
///   API pull): among owned rows, how many carry a <c>ServiceName</c> other than the true one.</item>
/// </list>
/// Pure aggregation — unit-testable.
/// </summary>
public static class ServiceNameMismatchCheck
{
    /// <summary>Per-table, per-<c>ServiceName</c> row counts.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Histogram(
        IEnumerable<ServiceNameRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(r => r.Table, StringComparer.Ordinal)
            .ToDictionary(
                table => table.Key,
                table => (IReadOnlyDictionary<string, int>)table
                    .GroupBy(r => r.ServiceName, StringComparer.Ordinal)
                    .ToDictionary(sn => sn.Key, sn => sn.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Among rows whose <see cref="ServiceNameRow.OwnerId"/> is in <paramref name="ownedIds"/>, count
    /// per table those whose <c>ServiceName</c> is not <paramref name="trueServiceName"/>. This is the
    /// only sound A3 mismatch signal, because ownership is established by the external-id set, not by
    /// the (potentially wrong) <c>ServiceName</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, int> CountOwnedMismatches(
        IEnumerable<ServiceNameRow> rows,
        IReadOnlySet<string> ownedIds,
        string trueServiceName)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(ownedIds);
        ArgumentException.ThrowIfNullOrEmpty(trueServiceName);

        return rows
            .Where(r => r.OwnerId is not null && ownedIds.Contains(r.OwnerId))
            .Where(r => !string.Equals(r.ServiceName, trueServiceName, StringComparison.Ordinal))
            .GroupBy(r => r.Table, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }
}