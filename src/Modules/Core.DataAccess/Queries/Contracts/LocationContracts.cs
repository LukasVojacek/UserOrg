using Core.Domain.Shared;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Optional filters for listing locations. <see cref="ScopeShortNames"/> is the caller (user) read-scope
/// tier: when set, only locations reachable from those projects (project → assigned resource → assigned
/// location) are returned. Admin leaves it <c>null</c> (all locations). The provider tier is that same caller
/// tier plus <see cref="OwnProviderName"/>, whose locations stay visible without a project link; it is an
/// addition to the tier, never a replacement for it. <see cref="ProviderName"/> is the user-facing search
/// filter and narrows whatever the tier allows. <c>null</c> members are ignored.
/// </summary>
public sealed record LocationFilter(
    ProviderName? ProviderName,
    ELocationType? LocationType,
    IReadOnlyList<ProjectShortName>? ScopeShortNames = null,
    ProviderName? OwnProviderName = null);

public sealed record LocationSummary(
    string Name,
    string? Description,
    string Endpoint,
    bool IsPrivate,
    bool IsShared,
    ELocationType LocationType,
    string ProviderName,
    EOperationMode Mode,
    string? DefaultServiceIntegrationUrl);

public sealed record LocationBaseData(
    string Name,
    string Endpoint);