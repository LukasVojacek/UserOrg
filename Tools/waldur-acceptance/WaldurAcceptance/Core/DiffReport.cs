namespace UserOrg.Tools.WaldurAcceptance.Core;

/// <summary>Classification verdict for one delta.</summary>
public enum DeltaClassification
{
    /// <summary>Matched a pre-approved ADR-0059/A1 entry.</summary>
    ExpectedDifference,

    /// <summary>No rule matched — must be explained or fixed before cutover.</summary>
    Unexplained,
}

/// <summary>
/// Three-state acceptance verdict. Expected differences are deliberately NOT a clean pass: each one
/// still needs a human to confirm it is really the known ADR-0059/A1 scenario before B14 deletes the
/// old code, so a scripted gate cannot go green on them.
/// </summary>
public enum AcceptanceOutcome
{
    /// <summary>Zero deltas — every row equal. Exit 0.</summary>
    Clean,

    /// <summary>Only expected differences, no unexplained. Exit 1 — needs human sign-off.</summary>
    NeedsConfirmation,

    /// <summary>One or more unexplained deltas. Exit 2.</summary>
    Fail,
}

/// <summary>A delta together with its verdict.</summary>
public sealed record ClassifiedDelta(RowDelta Delta, DeltaClassification Classification, string? Reason);

/// <summary>Outcome of an OLD-vs-NEW natural-key comparison.</summary>
public sealed record DiffReport(int EqualRowCount, IReadOnlyList<ClassifiedDelta> Deltas)
{
    /// <summary>Deltas explained by the expected-differences list.</summary>
    public IReadOnlyList<ClassifiedDelta> Expected { get; } =
        [.. Deltas.Where(d => d.Classification == DeltaClassification.ExpectedDifference)];

    /// <summary>Deltas nothing explained — the acceptance blockers.</summary>
    public IReadOnlyList<ClassifiedDelta> Unexplained { get; } =
        [.. Deltas.Where(d => d.Classification == DeltaClassification.Unexplained)];

    /// <summary>The three-state verdict. Expected differences never yield <see cref="AcceptanceOutcome.Clean"/>.</summary>
    public AcceptanceOutcome Outcome =>
        Unexplained.Count > 0 ? AcceptanceOutcome.Fail
        : Expected.Count > 0 ? AcceptanceOutcome.NeedsConfirmation
        : AcceptanceOutcome.Clean;
}