namespace Core.Shared.Paging;

/// <summary>
/// Configurable bounds for offset paging on growable v2 list endpoints, bound from the
/// <see cref="SectionName"/> configuration section (ADR-0020). <see cref="DefaultLimit"/> applies when
/// <c>?limit=</c> is absent or invalid; <see cref="MaxLimit"/> caps any larger request so a client cannot
/// pull an unbounded result set. Defaults come from <see cref="PageRequest.DefaultLimit"/>/<see cref="PageRequest.MaxLimit"/>.
/// </summary>
internal sealed class PaginationOptions
{
    public const string SectionName = "Pagination";

    public int DefaultLimit { get; init; } = PageRequest.DefaultLimit;

    public int MaxLimit { get; init; } = PageRequest.MaxLimit;
}