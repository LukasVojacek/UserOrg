using Waldur.Contracts.Payloads;

namespace Waldur.Sync.Diff;

/// <summary>
/// One iteration's mapped state for a single instance — the pure input to <see cref="DiffEngine"/>.
/// Everything here is already mapped/validated by the producer edge (B03); the diff only compares it
/// against the SHA map and decides which events to emit.
/// </summary>
public sealed record DiffInput
{
    /// <summary>Providers seen this iteration (one entry per fetched customer, eligible or not).</summary>
    public IReadOnlyList<ProviderDiffInput> Providers { get; init; } = [];

    /// <summary>Projects seen this iteration (one entry per fetched project, eligible or not).</summary>
    public IReadOnlyList<ProjectDiffInput> Projects { get; init; } = [];

    /// <summary>
    /// False when the customer list fetch was partial (a page failed, ADR-0064): the provider absence
    /// sweep is then suppressed, since a missing provider may merely sit on the un-fetched page.
    /// Defaults to true (a complete list, the backward-compatible case).
    /// </summary>
    public bool ProvidersComplete { get; init; } = true;

    /// <summary>
    /// False when the project list fetch was partial (a page failed, ADR-0064): the project absence
    /// sweep is then suppressed, since a missing project may merely sit on the un-fetched page.
    /// Defaults to true (a complete list, the backward-compatible case).
    /// </summary>
    public bool ProjectsComplete { get; init; } = true;

    /// <summary>
    /// The instance's storage-config payload, or null for non-EFP instances (which never emit a
    /// <c>StorageConfigurationChanged</c>).
    /// </summary>
    public StorageConfigPayload? StorageConfig { get; init; }
}

/// <summary>
/// A provider seen this iteration. <see cref="Payload"/> is null when the customer was skipped by
/// mapping (ineligible/invalid); the <see cref="ExternalId"/> is still carried so an ineligible
/// provider is treated as present, not as an absence transition.
/// </summary>
public sealed record ProviderDiffInput(string ExternalId, ProviderPayload? Payload)
{
    /// <summary>A provider carried forward this iteration (present, null payload) — ADR-0062.</summary>
    public static ProviderDiffInput CarriedForward(string externalId) => new(externalId, null);
}

/// <summary>
/// A project seen this iteration. <see cref="Payload"/> is null when the project was skipped by
/// mapping — an <b>ineligible</b> project (ADR-0054/A4), which produces no events at all including
/// membership. <see cref="Membership"/> is null when no member snapshot was taken for it (ineligible,
/// removed, or fetch failed).
/// </summary>
public sealed record ProjectDiffInput(
    string ExternalId,
    ProjectPayload? Payload,
    MembershipSnapshot? Membership = null)
{
    /// <summary>A project carried forward this iteration (present, null payload) — ADR-0062.</summary>
    public static ProjectDiffInput CarriedForward(string externalId, MembershipSnapshot? membership) =>
        new(externalId, null, membership);
}

/// <summary>
/// A project's <c>list_users</c> result. Membership is only diffed when the snapshot is a
/// <b>complete, successfully fetched</b> authoritative list (D7 / ADR-0054/A2); a failed snapshot
/// yields no membership events and leaves the member SHAs untouched. "Complete" at this boundary
/// means the worker set <see cref="IsComplete"/> to true because the API call succeeded and returned
/// a full (non-truncated) page set — the client throws on partial pagination, so a successful return
/// is by construction complete.
/// </summary>
/// <param name="IsComplete">True iff the snapshot was fetched successfully and in full.</param>
/// <param name="Members">The members in the snapshot (empty when complete but the project has none).</param>
public sealed record MembershipSnapshot(bool IsComplete, IReadOnlyList<ProjectUserPayload> Members)
{
    /// <summary>A successfully fetched, complete snapshot.</summary>
    public static MembershipSnapshot Complete(IReadOnlyList<ProjectUserPayload> members) => new(true, members);

    /// <summary>A failed member fetch — no membership events, SHAs untouched (D7).</summary>
    public static MembershipSnapshot Failed() => new(false, []);
}