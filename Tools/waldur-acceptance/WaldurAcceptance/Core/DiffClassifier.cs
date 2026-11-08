namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>
/// The pure heart of the acceptance harness. Given the OLD and NEW natural-key projections, an
/// exclusion list and the expected-differences list, it pairs rows by <see cref="NaturalKeyRow.Identity"/>,
/// emits a delta for every unpaired row or non-excluded field mismatch, and classifies each delta
/// as an expected difference or an unexplained one. No I/O — exhaustively unit-testable.
/// </summary>
public static class DiffClassifier
{
    public static DiffReport Classify(
        IReadOnlyCollection<NaturalKeyRow> oldRows,
        IReadOnlyCollection<NaturalKeyRow> newRows,
        IReadOnlySet<string> excludedFields,
        IReadOnlyList<ExpectedDifference> expectedDifferences)
    {
        ArgumentNullException.ThrowIfNull(oldRows);
        ArgumentNullException.ThrowIfNull(newRows);
        ArgumentNullException.ThrowIfNull(excludedFields);
        ArgumentNullException.ThrowIfNull(expectedDifferences);

        // GroupBy (not ToDictionary) so a duplicated natural key on either side is reported as a
        // DuplicateIdentity delta to investigate, never a thrown ArgumentException aborting the run.
        var oldByIdentity = oldRows.ToLookup(r => r.Identity, StringComparer.Ordinal);
        var newByIdentity = newRows.ToLookup(r => r.Identity, StringComparer.Ordinal);
        var identities = oldByIdentity.Select(g => g.Key)
            .Union(newByIdentity.Select(g => g.Key), StringComparer.Ordinal);

        var deltas = new List<RowDelta>();
        var equal = 0;

        foreach (var identity in identities)
        {
            var olds = oldByIdentity[identity].ToList();
            var news = newByIdentity[identity].ToList();

            if (olds.Count > 1 || news.Count > 1)
            {
                var sample = olds.FirstOrDefault() ?? news[0];
                deltas.Add(new RowDelta(sample.EntityType, sample.Key, DeltaKind.DuplicateIdentity,
                    olds.FirstOrDefault(), news.FirstOrDefault()));
                continue;
            }

            var oldRow = olds.FirstOrDefault();
            var newRow = news.FirstOrDefault();

            if (oldRow is null)
            {
                deltas.Add(new RowDelta(newRow!.EntityType, newRow.Key, DeltaKind.OnlyInNew, null, newRow));
                continue;
            }

            if (newRow is null)
            {
                deltas.Add(new RowDelta(oldRow.EntityType, oldRow.Key, DeltaKind.OnlyInOld, oldRow, null));
                continue;
            }

            var fieldDeltas = CompareFields(oldRow, newRow, excludedFields);
            if (fieldDeltas.Count == 0)
            {
                equal++;
            }
            else
            {
                deltas.AddRange(fieldDeltas);
            }
        }

        var classified = deltas.Select(d => ClassifyOne(d, expectedDifferences)).ToList();
        return new DiffReport(equal, classified);
    }

    private static List<RowDelta> CompareFields(
        NaturalKeyRow oldRow,
        NaturalKeyRow newRow,
        IReadOnlySet<string> excludedFields)
    {
        var deltas = new List<RowDelta>();
        var fields = oldRow.Fields.Keys.Union(newRow.Fields.Keys, StringComparer.Ordinal);

        foreach (var field in fields)
        {
            if (excludedFields.Contains(field))
            {
                continue;
            }

            var oldValue = oldRow.Fields.GetValueOrDefault(field);
            var newValue = newRow.Fields.GetValueOrDefault(field);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                deltas.Add(new RowDelta(
                    oldRow.EntityType, oldRow.Key, DeltaKind.FieldMismatch, oldRow, newRow, field, oldValue, newValue));
            }
        }

        return deltas;
    }

    private static ClassifiedDelta ClassifyOne(RowDelta delta, IReadOnlyList<ExpectedDifference> expectedDifferences)
    {
        foreach (var rule in expectedDifferences)
        {
            if (rule.Matches(delta))
            {
                return new ClassifiedDelta(delta, DeltaClassification.ExpectedDifference, $"{rule.Id}: {rule.Reason}");
            }
        }

        return new ClassifiedDelta(delta, DeltaClassification.Unexplained, null);
    }
}