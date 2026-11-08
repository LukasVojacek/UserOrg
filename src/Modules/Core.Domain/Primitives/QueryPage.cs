namespace Core.Domain.Primitives;

/// <summary>
/// One page of read-side rows plus the total count of rows matching the (already filtered) query, before
/// paging. The query layer's counterpart to the presentation envelope: it carries no page-number metadata —
/// the use-case derives that from the requested page/limit and <see cref="TotalItems"/>.
/// </summary>
public sealed record QueryPage<T>(IReadOnlyList<T> Items, int TotalItems);