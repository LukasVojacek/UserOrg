namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// Per-instance consumer configuration a handler needs but the payload does NOT carry — the
/// config-derived, non-Waldur values (plan §Options / ADR-0057/A5 extension). A superset of
/// <see cref="WaldurServiceAccountIdentity"/> in spirit: resolved by <see cref="ServiceName"/>, so
/// a single shared handler stays payload-driven for Waldur data while reading instance config here.
/// </summary>
/// <param name="ServiceName">The upsert discriminator ("EFP-Waldur" / "Puhuri-Waldur") — the resolver key.</param>
/// <param name="ServiceProjectDomainName">Name of the <c>ProjectDomainType</c> assigned to created projects (required FK).</param>
/// <param name="WaldurPreferredAuthType">HEAppE cluster auth type to select when initializing a project (e.g. "SshCertificate").</param>
/// <param name="WaldurPiEmail">Principal-investigator email stamped on the HEAppE project spec.</param>
internal sealed record WaldurConsumerInstanceOptions(
    string ServiceName,
    string ServiceProjectDomainName,
    string WaldurPreferredAuthType,
    string WaldurPiEmail);

/// <summary>
/// Resolves <see cref="WaldurConsumerInstanceOptions"/> by service name. Consumer handlers
/// (B08–B10) take this port instead of hardcoding per-platform constants (ADR-0057: shared
/// handlers, no parallel class hierarchies).
/// </summary>
internal interface IWaldurConsumerOptions
{
    /// <summary>Returns the options for <paramref name="serviceName"/>; throws when none registered (not acked → replay).</summary>
    WaldurConsumerInstanceOptions For(string serviceName);
}

/// <summary>
/// Dictionary-backed <see cref="IWaldurConsumerOptions"/> populated from the registered per-instance
/// options (bound from <c>Waldur:Instances[*]</c> at composition). A missing service name throws so
/// a mis-registered instance surfaces loudly rather than silently mutating with default config.
/// </summary>
internal sealed class WaldurConsumerOptions : IWaldurConsumerOptions
{
    private readonly IReadOnlyDictionary<string, WaldurConsumerInstanceOptions> _byServiceName;

    public WaldurConsumerOptions(IEnumerable<WaldurConsumerInstanceOptions> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _byServiceName = instances.ToDictionary(i => i.ServiceName, StringComparer.OrdinalIgnoreCase);
    }

    public WaldurConsumerInstanceOptions For(string serviceName) =>
        _byServiceName.TryGetValue(serviceName, out var options)
            ? options
            : throw new InvalidOperationException(
                $"No Waldur consumer options registered for service '{serviceName}'.");
}