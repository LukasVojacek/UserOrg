namespace Waldur.Sync.Diff;

/// <summary>
/// The in-memory SHA breadcrumb map for one instance worker (ADR-0055). Access is single-threaded —
/// the worker is sequential — so there are deliberately no concurrency primitives. Populated by a
/// cold rebuild on startup (<see cref="IShaMapRebuilder"/>) and then advanced only by applying the
/// mutations the <see cref="DiffEngine"/> returns, after each event is acked.
/// </summary>
public sealed class ShaBreadcrumbMap
{
    private readonly Dictionary<ShaKey, string> _entries;

    /// <summary>Creates an empty map.</summary>
    public ShaBreadcrumbMap() => _entries = [];

    /// <summary>Creates a map seeded from a cold rebuild.</summary>
    public ShaBreadcrumbMap(IReadOnlyDictionary<ShaKey, string> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _entries = new Dictionary<ShaKey, string>(seed);
    }

    /// <summary>Number of tracked entries.</summary>
    public int Count => _entries.Count;

    /// <summary>All tracked keys — used by the diff for the provider/project absence sweep.</summary>
    public IReadOnlyCollection<ShaKey> Keys => _entries.Keys;

    /// <summary>Reads the current hash for <paramref name="key"/>, if present.</summary>
    public bool TryGetHash(ShaKey key, out string hash) => _entries.TryGetValue(key, out hash!);

    /// <summary>Reads the current hash for <paramref name="key"/>, or null if absent.</summary>
    public string? GetHashOrNull(ShaKey key) => _entries.TryGetValue(key, out var hash) ? hash : null;

    /// <summary>Applies one deferred mutation (after the corresponding event was acked).</summary>
    public void Apply(ShaMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        switch (mutation.Kind)
        {
            case ShaMutationKind.Set:
            _entries[mutation.Key] = mutation.Hash
                ?? throw new ArgumentException("A Set mutation must carry a hash.", nameof(mutation));
            break;
            case ShaMutationKind.Remove:
            _entries.Remove(mutation.Key);
            break;
            default:
            throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    /// <summary>
    /// Drops an entry directly. The worker uses this after logging an absence transition
    /// (ADR-0058/A1) so the warning fires on transition only and never re-fires for the same key.
    /// </summary>
    public void Remove(ShaKey key) => _entries.Remove(key);
}