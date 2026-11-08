namespace Core.DataAccess.Queries.Contracts;

/// <summary>Optional filters for listing requests; null members are ignored.</summary>
/// <remarks>
/// <see cref="ScopeShortNames"/> restricts results to the given projects (caller scope, taken from
/// the internal token's <c>projectRoles</c> claim): <c>null</c> = no scoping, an empty list = no
/// projects match. <see cref="SenderIdpId"/> restricts to requests created by the caller, matched on
/// the <c>User.IdentityProviderUserId</c> — no <c>IdpId → UserId</c> round-trip required.
/// </remarks>
public sealed record RequestFilter(
    ProjectShortName? ProjectShortName,
    IdpId? UserIdpId,
    int? State,
    IReadOnlyList<ProjectShortName>? ScopeShortNames = null,
    IdpId? SenderIdpId = null);

public sealed record RequestSummary(
    Email? ContactEmail,
    RequestType Type,
    RequestState State,
    ProjectShortName ProjectShortName,
    IdpId IdpUserId,
    ProviderName? ProviderName,
    ResourceName? ProjectResourceName,
    string? Slug = null);

/// <summary>The request and action a single action-link token resolves to (write path / public handle).</summary>
public sealed record ActionLinkTarget(
    Guid RequestId,
    int Action,
    DateTimeOffset ValidTo);

public sealed record PublicRequestSummary(
    Guid RequestId,
    string? RequestorUserName,
    string? RequestorEmail,
    ProjectShortName ProjectShortName,
    string? ProjectResourceName,
    string? ProviderName,
    int RequestState,
    DateTimeOffset ValidTo,
    int RequestType,
    string? AcceptActionLink,
    string? RejectActionLink,
    string? Slug = null);