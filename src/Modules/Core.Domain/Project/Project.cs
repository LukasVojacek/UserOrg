using System.Collections.Immutable;

using Core.Domain.Shared;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Project;

/// <summary>
/// Project Aggregate Root (Natural ID: <see cref="ShortName"/>).
///
/// Pure domain model — holds only project state and behavior, no persistence concerns.
/// Id, CreatedAt and ModifiedAt are EF shadow properties; the FK columns
/// (CreatedByUserId, ProjectDomainTypeId) are mapped in <c>ProjectConfiguration</c>, not here.
///
/// Backing fields (<c>_projectTags</c>, <c>_assignedUsers</c>, <c>_assignedResources</c>,
/// <c>_projectDomainType</c>) are mapped by EF; the public projections expose read-only views.
/// </summary>
public sealed class Project : AggregateRoot, ISoftDeletableEntity, IServiceIntegrationEntity, IHasValidityPeriod
{
    private readonly ICollection<ProjectTag> _projectTags = [];
    private readonly ICollection<AssignedUserToProject> _assignedUsers = [];
    private readonly ICollection<AssignedProjectResourceToProject> _assignedResources = [];
    // Project role rows (UserPermission, ProjectId set) — coordinated with membership as one unit (ADR-0008).
    // Hydrated by the repository; mutated synchronously by the role methods. EF backing field.
    private readonly ICollection<UserPermission> _memberRoles = [];
    private ProjectDomainType _projectDomainType = default!;

    /// <summary>Natural identifier — unique, immutable short name.</summary>
    public ProjectShortName ShortName { get; private set; }

    public ProjectName Name { get; private set; }

    /// <summary>Project description. Null when not set (the column is nullable in the DB).</summary>
    public Description? Description { get; private set; }

    /// <summary>Contact email for the project. Null when not set (the column is nullable in the DB).</summary>
    public Email? Email { get; private set; }

    /// <summary>External identifier from an integrated service. Null when not assigned.</summary>
    public ExternalId? ExternalId { get; private set; }

    public EOperationMode Mode { get; private set; }

    /// <summary>
    /// Project lifecycle state, orthogonal to Mode. Active (operational), Expired (past EndDate),
    /// or Deleted (soft-deleted). Created projects start in Active state.
    /// </summary>
    public EProjectState State { get; private set; } = EProjectState.Active;

    public ServiceName ServiceName { get; private set; }

    public DateTimeOffset StartDate { get; private set; }

    public DateTimeOffset EndDate { get; private set; }

    /// <summary>Domain type of the project, exposed as its name; the int FK lives only in the config.</summary>
    public DomainTypeName DomainType => _projectDomainType.Name;

    /// <summary>Cross-aggregate reference to the creating <c>User</c>.</summary>
    public UserId CreatedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>Tags currently assigned to the project (active only).</summary>
    public ImmutableList<TagName> Tags =>
        [.. _projectTags.Where(t => !t.IsDeleted).Select(t => t.Name)];

    /// <summary>Users currently assigned to the project (active only).</summary>
    public ImmutableList<ProjectUser> Users =>
        [.. _assignedUsers.Where(u => !u.IsDeleted).Select(u => new ProjectUser(u.User.UserName, u.User.Email))];

    /// <summary>Resources currently assigned to the project (active only).</summary>
    public ImmutableList<ResourceDetail> Resources =>
        [.. _assignedResources
            .Where(r => !r.IsDeleted && r.ProjectResource is not null)
            .Select(r => new ResourceDetail(r.ProjectResource!.ResourceName, r.ProjectResource.ProviderName, r.ProjectResource.StartDate, r.ProjectResource.EndDate))];

    /// <summary>
    /// IdpIds of the project's active members (one per active project role) — capture this <b>before</b>
    /// <see cref="Delete"/> to evict their UserInfo caches. Mirrors the old handler's affected-user list.
    /// </summary>
    public IReadOnlyList<IdpId> ActiveMemberIdpIds =>
        [.. _memberRoles.Where(r => !r.IsDeleted).Select(r => r.User.IdentityProviderId)];

    /// <summary>The user's active project role over the loaded <c>_memberRoles</c>, or null when none.
    /// Internal read affordance for unit tests (no per-user role is exposed publicly).</summary>
    internal ProjectRoleId? ActiveRoleOf(User.User user) =>
        _memberRoles.FirstOrDefault(r => !r.IsDeleted && r.User == user)?.SystemRoleId;

    // EF materialization + factory only; closes the implicit public parameterless constructor so
    // `new Project()` cannot bypass Create.
    private Project() { }

    /// <summary>
    /// Creates a new project assigned to an existing <paramref name="domainType"/> lookup entity.
    /// </summary>
    public static Project Create(
        ProjectShortName shortName,
        ProjectName name,
        ProjectDomainType domainType,
        UserId createdBy,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        ServiceName serviceName,
        EOperationMode mode = EOperationMode.Unspecified,
        Description? description = null,
        Email? email = null,
        ExternalId? externalId = null)
    {
        ArgumentNullException.ThrowIfNull(domainType);

        if (endDate < startDate)
        {
            throw new ArgumentException("StartDate must not be after EndDate", nameof(startDate));
        }

        var project = new Project
        {
            ShortName = shortName,
            Name = name,
            Description = description,
            Email = email,
            ExternalId = externalId,
            Mode = mode,
            ServiceName = serviceName,
            StartDate = startDate,
            EndDate = endDate,
            CreatedBy = createdBy,
            IsDeleted = false,
        };
        project._projectDomainType = domainType;

        project.AddDomainEvent(new ProjectCreatedEvent(shortName, name, createdBy));
        return project;
    }

    /// <summary>Returns an error when the project cannot be modified, otherwise null.
    /// Checks both Mode (ReadOnly) and State (not Deleted or Expired).</summary>
    private Error? EnsureNotReadOnly()
    {
        if (Mode == EOperationMode.ReadOnly)
            return ProjectErrors.ProjectIsReadOnly(ShortName);
        if (State == EProjectState.Deleted)
            return ProjectErrors.ProjectIsDeleted(ShortName);
        if (State == EProjectState.Expired)
            return ProjectErrors.ProjectIsExpired(ShortName);
        return null;
    }

    /// <summary>Renames the project.</summary>
    public Result<Error> Rename(ProjectName name)
    {
        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        Name = name;
        return Result<Error>.Success();
    }

    /// <summary>Updates the project description, or clears it by passing <c>null</c>.</summary>
    public Result<Error> UpdateDescription(Description? description)
    {
        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        Description = description;
        return Result<Error>.Success();
    }

    /// <summary>Changes the project's contact email, or clears it by passing <c>null</c>.</summary>
    public Result<Error> ChangeContactEmail(Email? email)
    {
        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        Email = email;
        return Result<Error>.Success();
    }

    /// <summary>Reschedules the project's validity period.</summary>
    public Result<Error> Reschedule(DateTimeOffset startDate, DateTimeOffset endDate)
    {
        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        if (endDate < startDate)
        {
            return Result<Error>.Failure(ProjectErrors.EndDateBeforeStartDate());
        }

        StartDate = startDate;
        EndDate = endDate;
        return Result<Error>.Success();
    }

    /// <summary>Changes the project's domain type.</summary>
    public Result<Error> ChangeDomainType(ProjectDomainType domainType)
    {
        ArgumentNullException.ThrowIfNull(domainType);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        _projectDomainType = domainType;
        return Result<Error>.Success();
    }

    /// <summary>
    /// Soft-deletes the project and cascade soft-deletes its members' project roles.
    /// Sets State to Deleted. Can delete projects in Active or Expired state.
    /// </summary>
    public Result<Error> Delete()
    {
        if (Mode == EOperationMode.ReadOnly)
            return Result<Error>.Failure(ProjectErrors.ProjectIsReadOnly(ShortName));

        if (State == EProjectState.Deleted)
            return Result<Error>.Failure(ProjectErrors.ProjectIsDeleted(ShortName));

        IsDeleted = true;
        State = EProjectState.Deleted;
        foreach (var role in _memberRoles.Where(r => !r.IsDeleted))
        {
            role.SoftDelete();
        }
        AddDomainEvent(new ProjectDeletedEvent(ShortName));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Transitions the project to Expired state. Typically called by a scheduled job when EndDate elapses.
    /// Returns error if project is already Deleted. Editable and hidden from active queries once expired.
    /// </summary>
    public Result<Error> Expire()
    {
        if (State == EProjectState.Deleted)
        {
            return Result<Error>.Failure(ProjectErrors.CannotExpireDeletedProject(ShortName));
        }

        State = EProjectState.Expired;
        return Result<Error>.Success();
    }

    /// <summary>
    /// Assigns a user to the project. Reactivates a previously removed assignment if present.
    /// </summary>
    public Result<Error> AssignUser(User.User user, UserId assignedBy)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        var existing = _assignedUsers.FirstOrDefault(a => a.User == user);
        if (existing is not null)
        {
            if (!existing.IsDeleted)
            {
                return Result<Error>.Failure(ProjectErrors.UserAlreadyAssigned(user.UserName));
            }

            existing.Reactivate(assignedBy);
            return Result<Error>.Success();
        }

        _assignedUsers.Add(AssignedUserToProject.Create(user, assignedBy));
        return Result<Error>.Success();
    }

    /// <summary>Removes a user's active assignment from the project.</summary>
    public Result<Error> UnassignUser(User.User user, UserId unassignedBy)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        var assignment = _assignedUsers.FirstOrDefault(a => !a.IsDeleted && a.User == user);
        if (assignment is null)
        {
            return Result<Error>.Failure(ProjectErrors.UserNotAssigned(user.UserName));
        }

        assignment.Remove(unassignedBy);
        return Result<Error>.Success();
    }

    /// <summary>
    /// Assigns a user to the project as a member <b>and</b> grants them <paramref name="roleId"/> as their
    /// project role, coordinating membership and the role row as one unit (ADR-0008). Fails with
    /// <c>UserAlreadyAssigned</c> when the user already has an active membership. Blocked when read-only.
    /// </summary>
    public Result<Error> AssignUserWithRole(User.User target, UserId assignedBy, ProjectRoleId roleId)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (AssignUser(target, assignedBy) is { IsSuccess: false } membership)
        {
            return membership;
        }

        UpsertMemberRole(target, roleId);
        return Result<Error>.Success();
    }

    /// <summary>
    /// Removes a user's membership and their project role together. The project owner cannot be removed.
    /// Fails with <c>UserNotAssigned</c> when there is no active membership. Blocked when read-only.
    /// </summary>
    public Result<Error> UnassignUserWithRole(User.User target, UserId unassignedBy, ProjectRoleId ownerRoleId)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        var role = _memberRoles.FirstOrDefault(r => !r.IsDeleted && r.User == target);
        if (role is not null && role.SystemRoleId == ownerRoleId)
        {
            return Result<Error>.Failure(ProjectErrors.OwnerCannotBeUnassigned());
        }

        if (UnassignUser(target, unassignedBy) is { IsSuccess: false } membership)
        {
            return membership;
        }

        role?.SoftDelete();
        return Result<Error>.Success();
    }

    /// <summary>
    /// Changes a member's project role, enforcing the relative hierarchy: the requester may only assign a
    /// role strictly below their own level and no lower than the member baseline, and a project may hold at
    /// most one owner. Blocked when read-only. On success, returns the member's role before the change
    /// (<see langword="null"/> if the member had no active role yet).
    /// </summary>
    public Result<ProjectRoleId?, Error> ChangeUserRole(User.User target, ProjectRoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<ProjectRoleId?, Error>.Failure(error);
        }

        if (IsOutsideAssignableRange(assignment))
        {
            return Result<ProjectRoleId?, Error>.Failure(ProjectErrors.CannotAssignEqualOrHigherRole());
        }

        var currentRole = _memberRoles.FirstOrDefault(r => !r.IsDeleted && r.User == target)?.SystemRoleId;
        if (WouldAddSecondOwner(assignment, currentRole))
        {
            return Result<ProjectRoleId?, Error>.Failure(ProjectErrors.DuplicateProjectOwner());
        }

        UpsertMemberRole(target, assignment.RoleId);
        return Result<ProjectRoleId?, Error>.Success(currentRole);
    }

    private static bool IsOutsideAssignableRange(ProjectRoleAssignment assignment) =>
        assignment.RoleLevel >= assignment.RequesterLevel || assignment.RoleLevel < assignment.MemberBaselineLevel;

    private bool WouldAddSecondOwner(ProjectRoleAssignment assignment, ProjectRoleId? currentRole) =>
        assignment.RoleId == assignment.OwnerRoleId
        && currentRole != assignment.OwnerRoleId
        && _memberRoles.Any(r => !r.IsDeleted && r.SystemRoleId == assignment.OwnerRoleId);

    /// <summary>
    /// Creates, reactivates, or retargets the user's project-role row in <c>_memberRoles</c> so that after
    /// the call the user has exactly one active role of <paramref name="roleId"/>. Mirrors the upsert the
    /// former <c>IProjectRoleHandler</c> performed, but over the loaded collection. Matches the row by the
    /// <c>User</c> reference, like membership.
    /// </summary>
    private void UpsertMemberRole(User.User target, ProjectRoleId roleId)
    {
        var existing = _memberRoles.FirstOrDefault(r => r.User == target);
        if (existing is null)
        {
            _memberRoles.Add(UserPermission.CreateProjectRole(target, roleId));
        }
        else if (existing.IsDeleted)
        {
            existing.Reactivate(roleId);
        }
        else
        {
            existing.ChangeRole(roleId);
        }
    }

    public bool HasTag(TagName tagName) => _projectTags.Any(t => !t.IsDeleted && t.Name.Equals(tagName));

    /// <summary>
    /// Assigns an existing catalogue tag to the project (the assign-existing path). The caller resolves the
    /// tag beforehand. Idempotent. Blocked when the project is read-only.
    /// </summary>
    public Result<Error> AssignExistingTag(ProjectTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        if (HasTag(tag.Name))
        {
            return Result<Error>.Success();
        }

        _projectTags.Add(tag);
        return Result<Error>.Success();
    }

    /// <summary>
    /// Removes the named tag's assignment from the project. The tracked collection drives EF's delete of the
    /// join row; the shared catalogue tag is never deleted. Idempotent. Blocked when the project is read-only.
    /// </summary>
    public Result<Error> RemoveTag(TagName tagName)
    {
        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        var existing = _projectTags.FirstOrDefault(t => t.Name.Equals(tagName));
        if (existing is not null)
        {
            _projectTags.Remove(existing);
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Assigns a resource to the project. Reactivates a previously removed link if present; a second active
    /// assignment of the same resource fails with <c>ResourceAlreadyAssigned</c>. The caller resolves the
    /// resource beforehand (the same instance EF loaded into the existing links, matched by reference like
    /// <see cref="AssignExistingTag"/>). Raises <see cref="ProjectResourceAssignmentChangedEvent"/>. Blocked
    /// when the project is read-only. The cross-aggregate guards (resource read-only, project non-shared
    /// invariant) live in the use-case.
    /// </summary>
    public Result<Error> AssignResource(ProjectResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        var existing = _assignedResources.FirstOrDefault(a => a.ProjectResource == resource);
        if (existing is not null)
        {
            if (!existing.IsDeleted)
            {
                return Result<Error>.Failure(ProjectErrors.ResourceAlreadyAssigned(resource.ResourceName));
            }

            existing.Reactivate();
        }
        else
        {
            _assignedResources.Add(AssignedProjectResourceToProject.Create(resource));
        }

        AddDomainEvent(new ProjectResourceAssignmentChangedEvent(ShortName, resource.ResourceName));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Removes a resource's active assignment from the project (soft-delete). Fails with
    /// <c>ResourceNotAssigned</c> when there is no active link. Raises
    /// <see cref="ProjectResourceAssignmentChangedEvent"/>. Blocked when the project is read-only.
    /// </summary>
    public Result<Error> RemoveResource(ProjectResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        var assignment = _assignedResources.FirstOrDefault(a => !a.IsDeleted && a.ProjectResource == resource);
        if (assignment is null)
        {
            return Result<Error>.Failure(ProjectErrors.ResourceNotAssigned(resource.ResourceName));
        }

        assignment.Delete();
        AddDomainEvent(new ProjectResourceAssignmentChangedEvent(ShortName, resource.ResourceName));
        return Result<Error>.Success();
    }

    /// <summary>Marks the project valid indefinitely by setting <see cref="EndDate"/> to the max value.</summary>
    public Result<Error> SetIndefiniteValidity(TimeProvider timeProvider, DateTimeOffset? startDate = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (EnsureNotReadOnly() is { } error)
        {
            return Result<Error>.Failure(error);
        }

        StartDate = startDate ?? timeProvider.GetUtcNow();
        EndDate = DateTimeOffset.MaxValue;
        return Result<Error>.Success();
    }
}