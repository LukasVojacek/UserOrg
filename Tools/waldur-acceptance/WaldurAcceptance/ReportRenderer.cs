using UserOrg.Tools.WaldurAcceptance.Core;

namespace UserOrg.Tools.WaldurAcceptance;

/// <summary>Renders the classified diff / data-check to the console and returns the process exit code.</summary>
internal static class ReportRenderer
{
    public static int Render(DiffReport report)
    {
        Console.WriteLine($"Equal rows: {report.EqualRowCount}");
        Console.WriteLine($"Expected differences: {report.Expected.Count}");
        Console.WriteLine($"Unexplained differences: {report.Unexplained.Count}");

        // Every expected difference is listed explicitly (entity, key, matched rule) so a human can
        // sign off on each — it never yields a bare "PASS".
        if (report.Expected.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("EXPECTED — SIGN OFF ON EACH (ADR-0059/A1; each could also be a real regression):");
            foreach (var group in report.Expected.GroupBy(d => d.Reason, StringComparer.Ordinal))
            {
                Console.WriteLine($"  [{group.Count()}] {group.Key}");
                foreach (var item in group)
                {
                    Console.WriteLine($"       {Describe(item.Delta)}");
                }
            }
        }

        if (report.Unexplained.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("UNEXPLAINED (must be explained or fixed before cutover):");
            foreach (var item in report.Unexplained)
            {
                Console.WriteLine($"  {Describe(item.Delta)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(Headline(report.Outcome));
        return ExitCode(report.Outcome);
    }

    public static int RenderDataCheck(
        string instance,
        string trueServiceName,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> histogram,
        IReadOnlyDictionary<string, int>? attributedMismatches)
    {
        Console.WriteLine($"Instance under audit: {instance}  (true ServiceName: {trueServiceName})");
        Console.WriteLine();
        Console.WriteLine("ServiceName distribution (rows per ServiceName, per table):");
        foreach (var (table, byServiceName) in histogram.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var breakdown = string.Join(", ", byServiceName
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
            Console.WriteLine($"  {table}: {breakdown}");
        }

        Console.WriteLine();
        if (attributedMismatches is null)
        {
            Console.WriteLine("NOTE: mismatch attribution requires the owned-id set. Buggy Puhuri rows carry the");
            Console.WriteLine("EFP ServiceName by definition, so ServiceName alone cannot identify them. Supply");
            Console.WriteLine("--owned-ids <file of external ids> (from the Waldur API pull) for the sound count.");
            return 0;
        }

        var total = attributedMismatches.Values.Sum();
        Console.WriteLine($"Attributed mismatch rows (owned by {instance}, ServiceName != {trueServiceName}): {total}");
        foreach (var (table, count) in attributedMismatches.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {table}: {count}");
        }

        Console.WriteLine();
        Console.WriteLine(total == 0
            ? "RESULT: CLEAN — no owned row carries a foreign ServiceName; the new upsert keys will match."
            : "RESULT: HITS — agree a one-time data fix with the team before enabling the new sync (ADR-0059/A3).");
        return total == 0 ? 0 : 1;
    }

    private static string Headline(AcceptanceOutcome outcome) => outcome switch
    {
        AcceptanceOutcome.Clean => "RESULT: CLEAN — every row is equal.",
        AcceptanceOutcome.NeedsConfirmation =>
            "RESULT: PASS WITH DIFFERENCES — CONFIRM EACH expected difference above before accepting (not a clean pass).",
        _ => "RESULT: FAIL — unexplained differences present; explain or fix before cutover.",
    };

    private static int ExitCode(AcceptanceOutcome outcome) => outcome switch
    {
        AcceptanceOutcome.Clean => 0,
        AcceptanceOutcome.NeedsConfirmation => 1,
        _ => 2,
    };

    private static string Describe(RowDelta delta) => delta.Kind switch
    {
        DeltaKind.FieldMismatch => $"{delta.EntityType} [{delta.Key}] {delta.Field}: old='{delta.OldValue}' new='{delta.NewValue}'",
        DeltaKind.OnlyInOld => $"{delta.EntityType} [{delta.Key}] only in OLD",
        DeltaKind.OnlyInNew => $"{delta.EntityType} [{delta.Key}] only in NEW",
        DeltaKind.DuplicateIdentity => $"{delta.EntityType} [{delta.Key}] DUPLICATE natural key — projects to >1 row",
        _ => $"{delta.EntityType} [{delta.Key}]",
    };
}