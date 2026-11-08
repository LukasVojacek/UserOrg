namespace Waldur.Sync.Diff;

/// <summary>Whether a mutation writes/overwrites a hash or drops the entry.</summary>
public enum ShaMutationKind
{
    /// <summary>Write or overwrite the hash for the key.</summary>
    Set,

    /// <summary>Drop the entry for the key.</summary>
    Remove
}

/// <summary>
/// A deferred change to the SHA breadcrumb map. The <see cref="DiffEngine"/> never mutates the map;
/// it returns these alongside each event so the worker applies them <b>only after</b> the handler
/// acks the event (ADR-0056 synchronous ack). A failed publish leaves the map untouched, so the
/// item is naturally retried next iteration.
/// </summary>
/// <param name="Key">The map entry the mutation targets.</param>
/// <param name="Kind">Set or Remove.</param>
/// <param name="Hash">The new hash for <see cref="ShaMutationKind.Set"/>; null for Remove.</param>
public sealed record ShaMutation(ShaKey Key, ShaMutationKind Kind, string? Hash)
{
    /// <summary>A set/overwrite mutation.</summary>
    public static ShaMutation Set(ShaKey key, string hash) => new(key, ShaMutationKind.Set, hash);

    /// <summary>A remove mutation.</summary>
    public static ShaMutation Remove(ShaKey key) => new(key, ShaMutationKind.Remove, null);
}