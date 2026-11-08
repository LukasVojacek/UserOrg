using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// One user to synchronise, with the project-scoped role entries to emit. Mirrors legacy
/// <c>UserInfoExtendedModel</c> (minus the role names, which the endpoint resolves). Every user appears
/// exactly once; <see cref="Roles"/> holds one entry per (user, project) pair the user has a project role for.
/// </summary>
public sealed record SyncUserView(
    Guid Id,
    string KeycloakSid,
    string? UserName,
    string? Email,
    bool IsDeleted,
    IReadOnlyList<SyncUserRoleView> Roles);

/// <summary>
/// A user's project-scoped role at one project (legacy <c>UserSystemRoleExtendedModel</c> minus the resolved
/// role name/permissions). <see cref="SystemRoleId"/> is the user's <c>UserPermission.SystemRoleId</c> for the
/// project; the endpoint maps it to a role name + permissions via the role service.
/// </summary>
public sealed record SyncUserRoleView(
    string ProjectShortName,
    string UserEmail,
    int SystemRoleId,
    IReadOnlyList<SyncResourceView> ProjectResources);

/// <summary>
/// A project resource and its locations (legacy <c>ProjectResourceDetailModel</c>).
/// </summary>
public sealed record SyncResourceView(
    string Name,
    string? Description,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    EProjectResourceState State,
    IReadOnlyList<SyncLocationView> Locations);

/// <summary>
/// A location assignment for a resource (legacy <c>LocationDetailModel</c>). <see cref="Values"/> carries the
/// assignment's specification name/value pairs.
/// </summary>
public sealed record SyncLocationView(
    string Name,
    string? Description,
    string Endpoint,
    bool IsPrivate,
    string Type,
    long? AllocationUnitCount,
    EAllocationUnitType AllocationUnitType,
    IReadOnlyList<SyncLocationValueView> Values);

public sealed record SyncLocationValueView(string Name, string Value);