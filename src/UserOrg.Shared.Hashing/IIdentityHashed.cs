namespace UserOrg.Shared.Hashing;

/// <summary>
/// Implemented by generated code for every <see cref="GenerateIdentityHashAttribute"/> type — a
/// short, stable identifier derived purely from the instance's own (non-<see cref="HashIgnoreAttribute"/>)
/// data, suitable for change detection, dedup keys, or cache keys.
/// </summary>
public interface IIdentityHashed
{
    /// <summary>SHA-256 hex digest of the type's canonical, order- and culture-independent rendering.</summary>
    string ComputeIdentityHash();
}