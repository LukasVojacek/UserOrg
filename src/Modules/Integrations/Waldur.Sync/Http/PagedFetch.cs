namespace Waldur.Sync.Http;

/// <summary>
/// The result of a <b>resilient</b> paginated list fetch (ADR-0064): the items gathered from the
/// pages that succeeded, plus <see cref="IsComplete"/>. <see cref="IsComplete"/> is false when a page
/// failed and the returned list is therefore partial — the absent ids may merely sit on the failed
/// page, so a partial list must never drive absence detection. Only the customer and project list
/// fetches return this shape; detail fetches keep throwing on any page failure.
/// </summary>
public sealed record PagedFetch<T>(IReadOnlyList<T> Items, bool IsComplete);