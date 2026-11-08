namespace Waldur.Sync.Configuration;

/// <summary>
/// Root options bound from the <c>Waldur:Instances</c> configuration array. Each element is one
/// enabled-or-disabled Waldur instance (EFP, Puhuri, …) with its own worker and SHA map.
/// </summary>
public sealed class WaldurSyncOptions
{
    /// <summary>Configuration section name: <c>Waldur</c>.</summary>
    public const string SectionName = "Waldur";

    /// <summary>The configured instances (0..N).</summary>
    public IList<WaldurInstanceOptions> Instances { get; } = [];

    /// <summary>The subset of instances whose <see cref="WaldurInstanceOptions.Enabled"/> is true.</summary>
    public IEnumerable<WaldurInstanceOptions> EnabledInstances => Instances.Where(i => i.Enabled);
}