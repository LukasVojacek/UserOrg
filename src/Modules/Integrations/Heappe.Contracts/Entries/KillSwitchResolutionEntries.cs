namespace Heappe.Contracts.Entries;

/// <summary>
/// A Core-owned Location together with the Provider that owns it, projected for the kill-switch admin paths.
/// </summary>
/// <remarks>Host-contract shape, not an API contract: the Heappe module holds only GUIDs, so it needs Core to
/// supply the names its admin surface speaks in — and the ids to store against. Neither <c>Provider.Name</c> nor
/// <c>Location.Name</c> has a unique index, so callers must treat a name match as potentially ambiguous
/// (ADR-0080 "Location resolution is fail-closed").</remarks>
/// <param name="LocationId">The location's internal id.</param>
/// <param name="LocationName">The location's display name. Not unique.</param>
/// <param name="ProviderId">The internal id of the provider owning the location.</param>
/// <param name="ProviderName">The owning provider's display name. Not unique.</param>
public sealed record ProviderLocationEntry(Guid LocationId, string LocationName, Guid ProviderId, string ProviderName);

/// <summary>
/// An internal <c>(ProjectId, ProjectResourceId)</c> pair, as a Heappe partition carries it.
/// </summary>
/// <param name="ProjectId">The project's internal id.</param>
/// <param name="ProjectResourceId">The project resource's internal id.</param>
public sealed record ProjectResourceIdEntry(Guid ProjectId, Guid ProjectResourceId);

/// <summary>
/// An internal <c>(ProjectId, ProjectResourceId)</c> pair resolved to the names Core knows it by.
/// </summary>
/// <param name="ProjectId">The project's internal id.</param>
/// <param name="ProjectResourceId">The project resource's internal id.</param>
/// <param name="ProjectShortName">The project's short name.</param>
/// <param name="ResourceName">The project resource's name.</param>
public sealed record ProjectResourceNameEntry(Guid ProjectId, Guid ProjectResourceId, string ProjectShortName, string ResourceName);

/// <summary>
/// A <c>(ProjectShortName, ResourceName)</c> pair awaiting resolution to internal ids — the names direction of
/// <see cref="ProjectResourceIdEntry"/>.
/// </summary>
/// <remarks>The kill-switch whitelist is authored in names, so a batch resolution request is a set of these.</remarks>
/// <param name="ProjectShortName">The project's short name, as the caller supplied it.</param>
/// <param name="ResourceName">The project resource's name, as the caller supplied it.</param>
public sealed record ProjectResourceNamePair(string ProjectShortName, string ResourceName);