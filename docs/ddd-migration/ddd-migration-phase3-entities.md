# Phase 3: Remove Public ID Properties from Entities (Implementation Guide)

**Status:** SUPERSEDED by [ADR 0061](../architecture/adr/0061-legacy-write-paths-client-generate-guid-pks.md) — that ADR
commits to client-generated `Guid` v7 identifiers, the opposite
direction from this document's shadow-property proposal below. Kept for historical reference; do
not implement.  
**Objective:** Convert public Id properties to EF shadow properties, add natural identifier properties  
**Deliverables:** Updated domain entities, value object changes, documentation of breaking changes

---

## 3.1 Understanding Shadow Properties

### What are Shadow Properties?

**Shadow Properties** are EF Core features where properties exist only in the EF model, not in the CLR class.

```csharp
// BEFORE: Public property in class
public class Project
{
    public ProjectId Id { get; set; }  // ← Part of C# class
}

// AFTER: Shadow property (EF only, not in C#)
public class Project
{
    // No public Id property!
    // But EF knows about it via configuration:
    // builder.Property<ProjectId>("Id");
}

// Access in domain code (DON'T do this):
var id = project.Id;  // ❌ COMPILER ERROR - property doesn't exist!

// Access in EF configuration (OK):
var id = context.Entry(project).Property<ProjectId>("Id").CurrentValue;  // ✅ Valid
```

---

## 3.2 Entity Changes: Complete Refactoring Plan

### User Entity Refactoring

**File:** `src/Core/Domain/Entities/User.cs`

#### BEFORE (Current)
```csharp
using UserOrg.Shared.Persistance.Abstractions.BaseEntities;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

public class User : AuditableEntity<UserId>
{
    public UserId Id { get; set; }  // ← PUBLIC - exposed everywhere
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    
    public IList<UserPermission> UserPermissions { get; private set; } = [];
    public IList<UserPreference> UserPreferences { get; private set; } = [];
}
```

#### AFTER (DDD + Shadow Properties)
```csharp
using UserOrg.Shared.Persistance.Abstractions.BaseEntities;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

/// <summary>
/// User Aggregate Root
/// 
/// Natural Identifier: Email (unique, immutable)
/// Id property is shadow (managed by EF only)
/// </summary>
public class User : AuditableEntity
{
    // NO public Id property - it's a shadow property in EF configuration
    
    /// <summary>
    /// Natural Identifier: User's email (unique)
    /// </summary>
    public string Email { get; private set; } = string.Empty;
    
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    
    /// <summary>
    /// Permissions assigned to this user (child entities)
    /// </summary>
    public IList<UserPermission> UserPermissions { get; private set; } = [];
    
    /// <summary>
    /// User preferences (child entities)
    /// </summary>
    public IList<UserPreference> UserPreferences { get; private set; } = [];
    
    // ===== Domain Methods (Business Logic) =====
    
    /// <summary>
    /// Factory method to create a new user with validation
    /// </summary>
    public static Result<User> Create(string email, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<User>("Email is required");
        
        if (!IsValidEmail(email))
            return Result.Failure<User>("Email format is invalid");
        
        if (string.IsNullOrWhiteSpace(firstName))
            return Result.Failure<User>("First name is required");
        
        if (string.IsNullOrWhiteSpace(lastName))
            return Result.Failure<User>("Last name is required");
        
        var user = new User
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
        
        return Result.Success(user);
    }
    
    /// <summary>
    /// Check if user has specific permission
    /// </summary>
    public bool HasPermission(string permissionName) =>
        UserPermissions.Any(p => p.PermissionName == permissionName && !p.IsDeleted);
    
    /// <summary>
    /// Assign permission to user with validation
    /// </summary>
    public Result AssignPermission(UserPermission permission)
    {
        if (UserPermissions.Any(p => p.PermissionName == permission.PermissionName && !p.IsDeleted))
            return Result.Failure($"User already has '{permission.PermissionName}' permission");
        
        UserPermissions.Add(permission);
        return Result.Success();
    }
    
    /// <summary>
    /// Remove permission from user with validation
    /// </summary>
    public Result RemovePermission(string permissionName)
    {
        var permission = UserPermissions.FirstOrDefault(p => p.PermissionName == permissionName && !p.IsDeleted);
        if (permission is null)
            return Result.Failure($"Permission '{permissionName}' not found");
        
        permission.IsDeleted = true;
        return Result.Success();
    }
    
    /// <summary>
    /// Update user profile information
    /// </summary>
    public Result UpdateProfile(string newFirstName, string newLastName)
    {
        if (string.IsNullOrWhiteSpace(newFirstName) || string.IsNullOrWhiteSpace(newLastName))
            return Result.Failure("Name fields cannot be empty");
        
        FirstName = newFirstName;
        LastName = newLastName;
        return Result.Success();
    }
    
    /// <summary>
    /// Deactivate user (soft delete)
    /// </summary>
    public Result Deactivate(string reason = "")
    {
        if (IsDeleted)
            return Result.Failure("User is already deactivated");
        
        IsDeleted = true;
        return Result.Success();
    }
    
    // ===== Private Helpers =====
    
    private static bool IsValidEmail(string email) =>
        email.Contains("@") && email.Contains(".");
}
```

---

### Project Entity Refactoring

**File:** `src/Core/Domain/Entities/Project.cs`

#### BEFORE (Current)
```csharp
public class Project : AuditableExtendedServiceIntegrationEntity<ProjectId>
{
    public ProjectId Id { get; set; }  // ← PUBLIC
    public string? Name { get; set; }
    public ProjectShortName ShortName { get; set; }
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    public IList<ProjectTag> ProjectTags { get; private set; } = [];
    public IList<AssignedProjectResourceToProject> AssignedProjectResources { get; private set; } = [];
    public IList<AssignedUserToProject> AssignedUsers { get; private set; } = [];
}
```

#### AFTER (DDD + Shadow Properties)
```csharp
using UserOrg.Shared.Persistance.Abstractions.BaseEntities;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

/// <summary>
/// Project Aggregate Root
/// 
/// Natural Identifier: ProjectShortName (unique, semantic)
/// Id property is shadow (managed by EF only)
/// </summary>
public class Project : AuditableExtendedServiceIntegrationEntity
{
    // NO public Id property - it's a shadow property in EF configuration
    
    /// <summary>
    /// Natural Identifier: Project short name (unique, user-friendly)
    /// </summary>
    public ProjectShortName ShortName { get; private set; } = null!;
    
    public string? Name { get; private set; }
    public string? Description { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    
    /// <summary>
    /// Tags associated with this project (child entities)
    /// </summary>
    public IList<ProjectTag> ProjectTags { get; private set; } = [];
    
    /// <summary>
    /// Resources assigned to this project (child entities)
    /// </summary>
    public IList<AssignedProjectResourceToProject> AssignedProjectResources { get; private set; } = [];
    
    /// <summary>
    /// Users assigned to this project (child entities)
    /// </summary>
    public IList<AssignedUserToProject> AssignedUsers { get; private set; } = [];
    
    // ===== Domain Methods (Business Logic) =====
    
    /// <summary>
    /// Factory method to create project with validation
    /// </summary>
    public static Result<Project> Create(
        ProjectShortName shortName,
        string name,
        DateTime startDate,
        DateTime endDate,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Project>("Project name is required");
        
        if (startDate >= endDate)
            return Result.Failure<Project>("Start date must be before end date");
        
        if (startDate < DateTime.UtcNow.Date)
            return Result.Failure<Project>("Start date cannot be in the past");
        
        var project = new Project
        {
            ShortName = shortName,
            Name = name,
            Description = description,
            StartDate = startDate,
            EndDate = endDate
        };
        
        return Result.Success(project);
    }
    
    /// <summary>
    /// Check if project is currently active (between start and end dates)
    /// </summary>
    public bool IsActive() =>
        DateTime.UtcNow >= StartDate && DateTime.UtcNow <= EndDate;
    
    /// <summary>
    /// Add tag to project with validation
    /// </summary>
    public Result AddTag(string tagName, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return Result.Failure("Tag name is required");
        
        if (tagName.Length > 50)
            return Result.Failure("Tag name cannot exceed 50 characters");
        
        // Check for duplicate tags
        if (ProjectTags.Any(t => t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure($"Tag '{tagName}' already exists on this project");
        
        ProjectTags.Add(new ProjectTag { TagName = tagName, Description = description });
        return Result.Success();
    }
    
    /// <summary>
    /// Remove tag from project
    /// </summary>
    public Result RemoveTag(string tagName)
    {
        var tag = ProjectTags.FirstOrDefault(t => t.TagName == tagName && !t.IsDeleted);
        if (tag is null)
            return Result.Failure($"Tag '{tagName}' not found");
        
        tag.IsDeleted = true;
        return Result.Success();
    }
    
    /// <summary>
    /// Assign user to project with validation
    /// </summary>
    public Result AssignUser(string userEmail, ProjectRole role)
    {
        if (string.IsNullOrWhiteSpace(userEmail))
            return Result.Failure("User email is required");
        
        // Check if already assigned
        if (AssignedUsers.Any(au => au.UserEmail == userEmail && !au.IsDeleted))
            return Result.Failure("User is already assigned to this project");
        
        // Business rule: Cannot assign admin role to active project
        if (IsActive() && role == ProjectRole.Admin)
            return Result.Failure("Cannot assign admin role to active project");
        
        AssignedUsers.Add(new AssignedUserToProject { UserEmail = userEmail, Role = role });
        return Result.Success();
    }
    
    /// <summary>
    /// Remove user from project with validation
    /// </summary>
    public Result RemoveUser(string userEmail)
    {
        var assignment = AssignedUsers.FirstOrDefault(au => au.UserEmail == userEmail && !au.IsDeleted);
        if (assignment is null)
            return Result.Failure("User is not assigned to this project");
        
        // Business rule: Cannot remove last admin
        var adminCount = AssignedUsers.Count(au => au.Role == ProjectRole.Admin && !au.IsDeleted);
        if (assignment.Role == ProjectRole.Admin && adminCount == 1)
            return Result.Failure("Cannot remove the only project administrator");
        
        assignment.IsDeleted = true;
        return Result.Success();
    }
    
    /// <summary>
    /// Delete project with validation
    /// </summary>
    public Result Delete()
    {
        // Cannot delete active project
        if (IsActive())
            return Result.Failure("Cannot delete active project");
        
        // Cannot delete if has assigned users
        if (AssignedUsers.Any(au => !au.IsDeleted))
            return Result.Failure("Cannot delete project with assigned users");
        
        IsDeleted = true;
        return Result.Success();
    }
    
    /// <summary>
    /// Update project dates with validation
    /// </summary>
    public Result UpdateDateRange(DateTime newStartDate, DateTime newEndDate)
    {
        if (newStartDate >= newEndDate)
            return Result.Failure("Start date must be before end date");
        
        if (IsActive())
            return Result.Failure("Cannot modify dates of active project");
        
        StartDate = newStartDate;
        EndDate = newEndDate;
        return Result.Success();
    }
    
    /// <summary>
    /// Get count of active users
    /// </summary>
    public int GetActiveUserCount() =>
        AssignedUsers.Count(au => !au.IsDeleted);
    
    /// <summary>
    /// Get count of assigned resources
    /// </summary>
    public int GetAssignedResourceCount() =>
        AssignedProjectResources.Count(ar => !ar.IsDeleted);
}

public enum ProjectRole
{
    Viewer = 0,
    Editor = 1,
    Admin = 2
}
```

---

### Child Entity Changes

#### ProjectTag (Child Entity)

**BEFORE:**
```csharp
public class ProjectTag : AuditableEntity<ProjectTagId>
{
    public ProjectTagId Id { get; set; }
    public string TagName { get; set; }
    public string? Description { get; set; }
}
```

**AFTER:**
```csharp
public class ProjectTag : AuditableEntity
{
    // No public Id - shadow property only
    // No ProjectId - shadow FK only
    
    public string TagName { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Domain methods
    public Result UpdateName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return Result.Failure("Tag name cannot be empty");
        
        TagName = newName;
        return Result.Success();
    }
}
```

#### UserPermission (Child Entity)

**BEFORE:**
```csharp
public class UserPermission : AuditableEntity<UserPermissionId>
{
    public UserPermissionId Id { get; set; }
    public string PermissionName { get; set; }
}
```

**AFTER:**
```csharp
public class UserPermission : AuditableEntity
{
    // No public Id - shadow property only
    // No UserId - shadow FK only
    
    public string PermissionName { get; set; } = string.Empty;
    public bool IsRestricted { get; set; }
    
    public bool IsActive() => !IsDeleted;
}
```

---

## 3.3 Base Classes Update

### PrimaryIdentifiableEntity Base Class

**File:** `src/Shared/Persistance/Abstractions/BaseEntities/PrimaryIdentifiableEntity.cs`

#### BEFORE
```csharp
public abstract class PrimaryIdentifiableEntity<TId> where TId : struct, IEquatable<TId>
{
    public TId Id { get; set; }  // ← Public property
}
```

#### AFTER
```csharp
public abstract class PrimaryIdentifiableEntity
{
    // NO public Id property
    // Id will be configured as shadow property in EF: builder.Property<TId>("Id")
    
    // If domain logic needs equality based on ID:
    // Must be done in subclass using shadow property access (not recommended in domain)
    
    public override bool Equals(object? obj)
    {
        if (obj is not PrimaryIdentifiableEntity other)
            return false;
        
        // Note: Can't compare IDs here without DbContext
        // Use domain-specific natural identifiers for equality instead
        return true;
    }
    
    public override int GetHashCode() =>
        base.GetHashCode();
}
```

### AuditableEntity Base Class

**File:** `src/Shared/Persistance/Abstractions/BaseEntities/AuditableEntity.cs`

#### BEFORE
```csharp
public abstract class AuditableEntity<TId> : PrimaryIdentifiableEntity<TId>
    where TId : struct, IEquatable<TId>
{
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
}
```

#### AFTER
```csharp
public abstract class AuditableEntity : PrimaryIdentifiableEntity
{
    // Shadow properties (configured in EF, not here):
    // - CreatedAt
    // - ModifiedAt
    // - IsDeleted
    
    // These are managed by DatabaseContext.UpdateAuditableEntities()
    public virtual DateTime CreatedAt { get; set; }
    public virtual DateTime? ModifiedAt { get; set; }
    public virtual bool IsDeleted { get; set; }
}
```

---

## 3.4 Value Object Changes

### ProjectId Value Object

**File:** `src/Core/Domain/ValueObjects/ProjectId.cs`

#### BEFORE (Using for public property)
```csharp
public readonly struct ProjectId : IEquatable<ProjectId>
{
    public Guid Value { get; }
    
    private ProjectId(Guid value) => Value = value;
    
    public static ProjectId Create(Guid value) => new(value);
    
    public bool Equals(ProjectId other) => Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is ProjectId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
}
```

#### AFTER (Shadow property - still exists but not public on entity)
```csharp
/// <summary>
/// ProjectId - Internal domain identifier for projects
/// Used as shadow property in EF, not exposed in domain model
/// </summary>
public readonly struct ProjectId : IEquatable<ProjectId>
{
    public Guid Value { get; }
    
    private ProjectId(Guid value) => Value = value;
    
    public static ProjectId Create(Guid value) => new(value == Guid.Empty ? Guid.NewGuid() : value);
    
    public static ProjectId NewId() => Create(Guid.NewGuid());
    
    public bool Equals(ProjectId other) => Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is ProjectId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
}
```

---

## 3.5 Migration Checklist

- [ ] Update `PrimaryIdentifiableEntity` - remove generic TId, make non-generic
- [ ] Update `AuditableEntity` - remove inheritance from PrimaryIdentifiableEntity<TId>
- [ ] Update `User.cs` - remove public Id, add domain methods
- [ ] Update `Project.cs` - remove public Id, add domain methods
- [ ] Update all child entity classes - remove public Id and FK properties
- [ ] Update value objects (ProjectId, UserId, etc.) - keep but internal
- [ ] Verify all 24+ entities updated
- [ ] Compilation check - fix any build errors
- [ ] Update unit tests - adjust for no direct ID access
- [ ] Create new domain event classes for important business events

---

## 3.6 Consequences & Breaking Changes

### What Breaks

| Item | Impact | Mitigation |
|------|--------|-----------|
| `entity.Id` access | ❌ Compiler error | Use natural identifiers instead |
| Equality comparisons | ⚠️ Must change | Use domain-specific identifiers |
| Serialization | ⚠️ JSON excludes Id | Update DTOs |
| Tests checking ID | ⚠️ Must update | Query by natural ID |
| Mapping code | ⚠️ Must refactor | Use natural identifiers |

### Benefits Gained

| Benefit | Impact |
|---------|--------|
| Clean domain model | ✅ No infrastructure leaks |
| Natural queries | ✅ Use ProjectShortName not UUID |
| Aggregate boundaries | ✅ Child entities not directly accessible |
| DDD principles | ✅ True domain-driven design |
| Testability | ✅ Domain logic testable without EF |

---

## 3.7 Testing Strategy

### Unit Tests - No Database

```csharp
[Fact]
public void Project_Create_WithValidData_Succeeds()
{
    // Arrange
    var shortName = ProjectShortName.Create("TST");
    
    // Act
    var result = Project.Create(
        shortName,
        "Test Project",
        DateTime.UtcNow.AddDays(1),
        DateTime.UtcNow.AddDays(10));
    
    // Assert
    Assert.True(result.IsSuccess);
    var project = result.Value;
    Assert.Equal("Test Project", project.Name);
    Assert.Equal(shortName, project.ShortName);
    // NO assertions on Id - it doesn't exist in domain!
}

[Fact]
public void Project_AddTag_WithDuplicate_Fails()
{
    // Arrange
    var project = CreateTestProject();
    
    // Act
    var result1 = project.AddTag("urgent");
    var result2 = project.AddTag("urgent");  // Duplicate
    
    // Assert
    Assert.True(result1.IsSuccess);
    Assert.False(result2.IsSuccess);
    Assert.Contains("already exists", result2.Error);
}
```

---

## 3.8 Next Steps

> **Superseded by [ADR 0061](../architecture/adr/0061-legacy-write-paths-client-generate-guid-pks.md):**
> identifiers stay public, client-generated `Guid` v7 values, not shadow properties. The steps
> below are historical and were not carried out.

After Phase 3 completion:
- ✅ All entities using shadow IDs
- ✅ Natural identifiers in place
- ✅ Domain methods encapsulated
- ✅ No public Id exposure
- ➡️ **Next: Phase 4 - EF Entity Configurations (HasAlternateKey, etc.)**

---

## References

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[Phase 2: DbContext Refactoring](./ddd-migration-phase2-dbcontext.md)** - Previous phase: Read/Write separation
- **[Phase 4: EF Configuration](./ddd-migration-phase4-ef-config.md)** - Next phase: Configure shadow properties (TBD)
- **[EF Core Shadow Properties](https://learn.microsoft.com/en-us/ef/core/modeling/shadow-properties)** - Microsoft documentation
- **[EF Core Value Objects](https://learn.microsoft.com/en-us/ef/core/modeling/value-objects)** - Microsoft documentation
- **[DDD Entity Design](https://www.domainlanguage.com/ddd/)** - Eric Evans
