using Waldur.Contracts.Payloads;
using Waldur.Sync.Api.WaldurApiModels;

namespace Waldur.Sync.Mapping;

/// <summary>
/// A structured reason an item (or sub-item) was not mapped. Ineligible/invalid items never throw
/// and never produce a payload — they surface here so the worker (B05) can log them per ADR-0058.
/// This re-expresses today's three validation layers (<c>AggregateDataValidator</c>,
/// <c>EnsureValidator</c>, and the inline producer/consumer skip rules) as mapping output.
/// </summary>
/// <param name="EntityType">"Provider", "Project", "Resource", "Location" or "Member".</param>
/// <param name="ExternalId">The Waldur id (or username) the skip relates to, canonical "D" form where a GUID.</param>
/// <param name="Reason">Machine-usable skip reason.</param>
/// <param name="Detail">Human-readable detail for logs.</param>
public sealed record MappingSkip(string EntityType, string ExternalId, EMappingSkipReason Reason, string Detail);

/// <summary>
/// One resource paired with its (fully-detailed) offering — the mapper's per-resource input. The
/// offering must be the <b>complete</b> <see cref="ApiPublicOffering"/> (endpoints + components +
/// plugin_options + category_title), because resource mapping needs all of them. A null offering is
/// treated as a fetch gap and the resource is skipped (<see cref="EMappingSkipReason.ResourceOfferingMissing"/>).
/// </summary>
public sealed record ResourceOffering(ApiMarketplaceResource Resource, ApiPublicOffering? Offering);

/// <summary>Provider mapping output: the payload (null when the provider was skipped) + skip reasons.</summary>
public sealed record ProviderMappingResult(ProviderPayload? Payload, IReadOnlyList<MappingSkip> Skips);

/// <summary>Project mapping output: the payload (null when the project was skipped) + skip reasons.</summary>
public sealed record ProjectMappingResult(ProjectPayload? Payload, IReadOnlyList<MappingSkip> Skips);

/// <summary>Membership mapping output: one payload per eligible member + skip reasons.</summary>
public sealed record MembershipMappingResult(IReadOnlyList<ProjectUserPayload> Members, IReadOnlyList<MappingSkip> Skips);