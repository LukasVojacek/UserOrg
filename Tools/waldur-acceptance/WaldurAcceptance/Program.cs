using UserOrg.Tools.WaldurAcceptance.Core;
using UserOrg.Tools.WaldurAcceptance.Sql;

namespace UserOrg.Tools.WaldurAcceptance;

/// <summary>
/// Entry point. Two modes, both args-in / console-report-out (no config framework, no DI):
/// <list type="bullet">
///   <item><c>diff --old &lt;conn&gt; --new &lt;conn&gt; [--efp-core-service-name &lt;name&gt;]</c>
///   — ADR-0059/A1 natural-key comparison of the two empty-DB parallel-run databases.</item>
///   <item><c>data-check --db &lt;conn&gt; --instance &lt;name&gt; --true-service-name &lt;name&gt;</c>
///   — ADR-0059/A3 ServiceName-mismatch count on a non-empty database.</item>
/// </list>
/// Exit code 0 = pass (diff fully explained / no mismatches), 1 = attention required, 2 = usage error.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        var options = ArgMap.Parse(args[1..]);
        return args[0] switch
        {
            "diff" => RunDiff(options),
            "data-check" => RunDataCheck(options),
            _ => Usage(),
        };
    }

    private static int RunDiff(ArgMap options)
    {
        if (!options.TryGet("old", out var oldConn) || !options.TryGet("new", out var newConn))
        {
            return Usage();
        }

        var efpCore = options.GetOrDefault("efp-core-service-name", "EFP-Core");
        var oldRows = new SqlUserOrgReader(oldConn, efpCore).ReadNaturalKeyRows();
        var newRows = new SqlUserOrgReader(newConn, efpCore).ReadNaturalKeyRows();

        var report = DiffClassifier.Classify(
            oldRows, newRows, ProjectionDefaults.ExcludedFields, ExpectedDifferences.All);

        return ReportRenderer.Render(report);
    }

    private static int RunDataCheck(ArgMap options)
    {
        if (!options.TryGet("db", out var conn) || !options.TryGet("true-service-name", out var trueName))
        {
            return Usage();
        }

        var instance = options.GetOrDefault("instance", "(unspecified)");
        var rows = new SqlUserOrgReader(conn, trueName).ReadServiceNameRows();
        var histogram = ServiceNameMismatchCheck.Histogram(rows);

        IReadOnlyDictionary<string, int>? attributed = null;
        if (options.TryGet("owned-ids", out var ownedIdsPath))
        {
            var ownedIds = LoadOwnedIds(ownedIdsPath);
            attributed = ServiceNameMismatchCheck.CountOwnedMismatches(rows, ownedIds, trueName);
        }

        return ReportRenderer.RenderDataCheck(instance, trueName, histogram, attributed);
    }

    private static HashSet<string> LoadOwnedIds(string path) =>
        File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static int Usage()
    {
        Console.Error.WriteLine("""
            waldur-acceptance — ADR-0059 parallel-run comparison + non-empty-DB data check.

              diff        --old <conn> --new <conn> [--efp-core-service-name <name>]
                          exit 0 = CLEAN, 1 = PASS WITH DIFFERENCES (confirm each), 2 = FAIL/usage.
              data-check  --db <conn> --instance <name> --true-service-name <name> [--owned-ids <path>]
                          prints a per-table ServiceName histogram; --owned-ids <file of external ids>
                          adds the sound attributed mismatch count.
            """);
        return 2;
    }
}
