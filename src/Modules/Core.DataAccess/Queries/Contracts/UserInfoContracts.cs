using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries.Contracts;

public sealed record UserDetails(IdpId IdentityProviderUserId, Email Email, UserName UserName);

public sealed record UserFilter(ProjectShortName? ProjectShortName, IdpId? IdentityProviderId);

public sealed record UserSummary(
    IdpId IdentityProviderId,
    Email Email,
    UserName UserName,
    IReadOnlyList<ProjectShortName> Projects,
    ProviderName? AssignedProviderName);

public sealed record UserExtendedDetails(
    IdpId IdentityProviderId,
    Email Email,
    UserName UserName,
    IReadOnlyList<UserProjectResourceData> ProjectResources);

public sealed record UserProjectResourceData(
    ProjectShortName ProjectShortName,
    string ResourceName,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    IReadOnlyList<AssignedLocationData> Locations);

public sealed record AssignedLocationData(
    string LocationName,
    string? Description,
    string Endpoint,
    bool IsPrivate,
    string LocationType,
    long? AllocationUnitCount,
    EAllocationUnitType AllocationUnitType,
    IReadOnlyList<(string Name, string Value)> Values);

public sealed record UserWithIdSummary(
    UserId UserId,
    IdpId IdentityProviderId,
    Email Email,
    UserName UserName,
    IReadOnlyList<ProjectShortName> Projects,
    ProviderName? AssignedProviderName);
