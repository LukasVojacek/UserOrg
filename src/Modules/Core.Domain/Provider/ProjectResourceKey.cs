namespace Core.Domain.Provider;

/// <summary>
/// Natural identifier of a <see cref="ProjectResource"/> (ADR-0044 §1): the pair
/// <c>(ProviderName, ResourceName)</c>. <see cref="ResourceName"/> alone is not unique — the same name
/// may exist under different providers (ADR-0026) — so the owning provider qualifies it. This is the
/// boundary-crossing identity other aggregates use; the surrogate <see cref="ProjectResourceId"/> stays
/// a storage detail. Equality is the record-struct default: equal when both <see cref="Provider"/> and
/// <see cref="Name"/> match.
/// </summary>
public readonly record struct ProjectResourceKey(ProviderName Provider, ResourceName Name);