using Core.Domain.Shared;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Optional filters for listing providers; null members are ignored. <see cref="OnlyPublic"/> is the
/// caller (user) read-scope tier: when set, private providers are hidden — except those reachable through
/// <see cref="ScopeShortNames"/> (the caller's projects), which stay visible so the list stays consistent
/// with the locations a user can already see (ADR-0018; the inverse chain
/// project → assigned resource → assigned location → location → provider). The admin tier leaves them unset.
/// The provider tier is that same caller tier plus <see cref="OwnProviderName"/>, which stays visible even
/// when it is private and no project reaches it; it is an addition to the tier, never a replacement for it.
/// <see cref="Name"/> is the user-facing search filter and narrows whatever the tier allows.
/// </summary>
public sealed record ProviderFilter(
    ProviderName? Name,
    string? CountryCode,
    bool OnlyPublic = false,
    IReadOnlyList<ProjectShortName>? ScopeShortNames = null,
    ProviderName? OwnProviderName = null);

/// <summary>
/// The set of administered providers: <c>Names == null</c> is every provider, an empty set is none.
/// <c>IncludeInactive</c> also returns deactivated providers flagged <c>IsDeleted</c>; deleted locations stay hidden.
/// </summary>
public sealed record AdministeredProviderFilter(IReadOnlyCollection<ProviderName>? Names, bool IncludeInactive = false);

public sealed record ProviderSummary(
    ProviderName Name,
    Description? Description,
    Email? SupportEmail,
    ExternalId? ExternalId,
    EOperationMode Mode,
    ServiceName ServiceName,
    bool IsPrivate,
    string CountryCode);

public sealed record ProviderLocationDetail(
    LocationName Name,
    string Endpoint,
    string? Description,
    bool IsPrivate,
    bool IsShared);

public sealed record ProviderDetail(
    ProviderName Name,
    Description? Description,
    Email? SupportEmail,
    ExternalId? ExternalId,
    EOperationMode Mode,
    ServiceName ServiceName,
    bool IsPrivate,
    string CountryCode,
    bool IsDeleted,
    IReadOnlyList<ProviderLocationDetail> Locations);