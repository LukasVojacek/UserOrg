using Lukdrasil.Error;

namespace Waldur.Sync.Worker;

/// <summary>
/// Expected-failure reasons at the producer fetch boundary (ADR-0062/0065). A detail fetch that
/// faults is an <b>expected</b> outcome the worker isolates per item (carry-forward), not a
/// programming error — so it is modelled as a <see cref="Lukdrasil.Result.Result{TValue,TError}"/>
/// failure carrying one of these, never as a thrown exception.
/// </summary>
internal static class WaldurSyncErrors
{
    /// <summary>A per-item detail fetch (offerings / resources / users) failed and is carried forward.</summary>
    internal static Error FetchFailed(string entityType, string externalId) => new ErrorBuilder()
        .WithCode("WaldurFetchFailed")
        .WithDescription("Detail fetch failed for {0} {1}; carried forward this iteration.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(entityType, externalId);
}