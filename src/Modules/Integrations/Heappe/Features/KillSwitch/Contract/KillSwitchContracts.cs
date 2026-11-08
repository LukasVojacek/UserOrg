namespace Heappe.Features.KillSwitch.Contract;

/// <summary>
/// A command template exempted from a Location's kill-switch, addressed by natural key.
/// </summary>
/// <remarks>Names only: the PUT resolves this triple to internal ids once and stores the whitelist by id
/// (ADR-0077 §1), so the read-path degrades stay in-module. Whitelisting covers all of the template's
/// aggregation partitions on the Location.</remarks>
/// <param name="ProjectShortName">The short name of the project owning the template.</param>
/// <param name="ResourceName">The name of the project resource the template belongs to.</param>
/// <param name="TemplateName">The command template's name.</param>
public sealed record KillSwitchWhitelistItem(string ProjectShortName, string ResourceName, string TemplateName);

/// <summary>
/// Request body for REPLACING a Location's standing whitelist (ADR-0013 §1).
/// </summary>
/// <remarks>Independent of the kill-switch's state: the whitelist is configuration prepared ahead of an
/// incident, so it is written whether or not the Location is currently frozen. Replace, not add/remove — the
/// admin UI holds the full list from the GET and posts what is ticked, so an empty list CLEARS the whitelist.</remarks>
/// <param name="Items">The templates that must keep running while the Location is frozen. Every item must exist on the Location, or the whole request is refused.</param>
public sealed record SetLocationWhitelistRequest(IReadOnlyList<KillSwitchWhitelistItem> Items);

/// <summary>
/// Request body for enforcing or lifting the kill-switch on ONE Location.
/// </summary>
/// <remarks>Carries NO whitelist: the whitelist is standing configuration written on its own endpoint
/// (ADR-0013 §2), and a freeze only ever reads it.</remarks>
/// <param name="IsEnforced">Whether launches on the Location are frozen. Lifting triggers a resync and leaves the whitelist standing.</param>
/// <param name="Reason">Why the Location is being frozen. Required when enforcing, minimum 10 characters; ignored when lifting.</param>
/// <param name="IgnoreWhitelist">Lockdown: freeze the Location's templates even when whitelisted, without deleting the whitelist (ADR-0013 §3). Only valid while enforcing; a lift always resets it.</param>
public sealed record SetLocationKillSwitchRequest(
    bool IsEnforced,
    string? Reason,
    bool IgnoreWhitelist = false);

/// <summary>
/// Request body for the provider-wide fast action (ADR-0082): enforce or lift across ALL of a provider's
/// current Locations.
/// </summary>
/// <remarks>Deliberately carries NO whitelist: this is the fastest incident action, so it has nothing to
/// validate and cannot fail on a bad whitelist entry. "Freeze everything, exceptions included" is
/// <paramref name="IgnoreWhitelist"/>, which suppresses the standing whitelists rather than deleting them.</remarks>
/// <param name="IsEnforced">Whether every current Location of the provider is frozen. Lifting leaves the whitelists standing.</param>
/// <param name="Reason">Why the provider is being frozen. Required when enforcing, minimum 10 characters; ignored when lifting.</param>
/// <param name="IgnoreWhitelist">Lockdown, fanned out to every Location of the provider (ADR-0013 §3). Only valid while enforcing; a lift always resets it.</param>
public sealed record SetProviderKillSwitchRequest(
    bool IsEnforced,
    string? Reason,
    bool IgnoreWhitelist = false);

/// <summary>
/// A Location the caller may manage, with its current kill-switch state.
/// </summary>
/// <param name="ProviderName">The name of the provider owning the Location.</param>
/// <param name="LocationName">The Location's name.</param>
/// <param name="IsEnforced">Whether the Location is currently frozen.</param>
/// <param name="Reason">Why the Location was frozen; <see langword="null"/> when it never was. Admin-only — never exposed on any other path.</param>
/// <param name="EnforcedAtUtc">When the kill-switch was last enforced; <see langword="null"/> when it never was. Admin-only.</param>
/// <param name="IgnoreWhitelist">Whether the current freeze ignores the Location's whitelist, so a ticked template reads as Frozen (ADR-0013 §5).</param>
public sealed record KillSwitchLocationState(
    string ProviderName,
    string LocationName,
    bool IsEnforced,
    string? Reason,
    DateTime? EnforcedAtUtc,
    bool IgnoreWhitelist);

/// <summary>
/// One command template's state on a frozen (or healthy) Location.
/// </summary>
/// <param name="ProjectShortName">The short name of the project owning the template.</param>
/// <param name="ResourceName">The name of the project resource the template belongs to.</param>
/// <param name="TemplateName">The command template's name.</param>
/// <param name="StoredState">The template's stored state on this Location, which a kill-switch never rewrites (ADR-0077).</param>
/// <param name="EffectiveState">What readers see: <c>Frozen</c> when the kill-switch kills it here, otherwise the stored state.</param>
/// <param name="Whitelisted">Whether the template is exempt from this Location's kill-switch.</param>
public sealed record KillSwitchTemplateState(
    string ProjectShortName,
    string ResourceName,
    string TemplateName,
    string StoredState,
    string EffectiveState,
    bool Whitelisted);

/// <summary>
/// A single Location's kill-switch state plus every command template on it.
/// </summary>
/// <param name="ProviderName">The name of the provider owning the Location.</param>
/// <param name="LocationName">The Location's name.</param>
/// <param name="IsEnforced">Whether the Location is currently frozen.</param>
/// <param name="Reason">Why the Location was frozen; <see langword="null"/> when it never was. Admin-only.</param>
/// <param name="EnforcedAtUtc">When the kill-switch was last enforced; <see langword="null"/> when it never was. Admin-only.</param>
/// <param name="IgnoreWhitelist">Whether the current freeze ignores the whitelist, so a template can be <c>Whitelisted</c> yet read <c>Frozen</c> (ADR-0013 §5).</param>
/// <param name="Templates">Every command template with a partition on this Location.</param>
public sealed record LocationKillSwitchDetail(
    string ProviderName,
    string LocationName,
    bool IsEnforced,
    string? Reason,
    DateTime? EnforcedAtUtc,
    bool IgnoreWhitelist,
    IReadOnlyList<KillSwitchTemplateState> Templates);

/// <summary>
/// A <c>(project, resource)</c> pair whose resync could not be queued.
/// </summary>
/// <param name="ProjectShortName">The short name of the project to resync.</param>
/// <param name="ResourceName">The name of the project resource to resync.</param>
public sealed record KillSwitchResyncItem(string ProjectShortName, string ResourceName);

/// <summary>
/// Outcome of enforcing or lifting the kill-switch on one Location.
/// </summary>
/// <param name="ProviderName">The name of the provider owning the Location.</param>
/// <param name="LocationName">The Location's name.</param>
/// <param name="IsEnforced">The kill-switch state now in effect.</param>
/// <param name="StillAllowed">The Location's whitelist as it stands AFTER this request — what an operator can still launch. Empty on a lift and under lockdown, where nothing is exempt even though the rows survive (ADR-0013 §4).</param>
/// <param name="UnqueuedResyncItems">Resyncs the bounded queue could not accept, so the admin can re-run them. Always empty when enforcing.</param>
public sealed record KillSwitchLocationUpdateResult(
    string ProviderName,
    string LocationName,
    bool IsEnforced,
    IReadOnlyList<KillSwitchWhitelistItem> StillAllowed,
    IReadOnlyList<KillSwitchResyncItem> UnqueuedResyncItems);

/// <summary>
/// Outcome of a provider-wide enforce or lift-all (ADR-0082).
/// </summary>
/// <remarks><paramref name="AffectedLocationCount"/> is a snapshot taken at action time: a Location added to the
/// provider afterwards is not covered, and the admin re-runs the action to include it.</remarks>
/// <param name="ProviderName">The provider's name.</param>
/// <param name="IsEnforced">The kill-switch state now in effect on every one of those Locations.</param>
/// <param name="AffectedLocationCount">How many of the provider's current Locations the action covered.</param>
/// <param name="StillAllowedCount">How many whitelist exceptions remain in force across THOSE Locations after this request; 0 on a lift and under lockdown. A count, not a list: the provider path is a deliberate fast fan-out and must not pay for name resolution across every Location (ADR-0013 §4).</param>
/// <param name="UnqueuedResyncItems">Resyncs the bounded queue could not accept, so the admin can re-run them. Always empty when enforcing.</param>
public sealed record KillSwitchProviderUpdateResult(
    string ProviderName,
    bool IsEnforced,
    int AffectedLocationCount,
    int StillAllowedCount,
    IReadOnlyList<KillSwitchResyncItem> UnqueuedResyncItems);