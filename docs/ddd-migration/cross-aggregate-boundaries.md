# Cross-Aggregate Boundaries in DDD

**Purpose:** Explain how to properly handle references between different aggregates while maintaining DDD boundaries  
**Created:** 2026-04-28  
**Status:** Guidance & Best Practices  
**Audience:** Developers implementing DDD patterns in the Core module

---

## 🎯 The Problem

In traditional ORM patterns, it's easy to create tight coupling between entities:

```csharp
// ❌ WRONG - Creates tight coupling between aggregates
public class User
{
    public List<Project> AssignedProjects { get; set; }  // Direct reference!
}

// ❌ WRONG - Domain code accessing aggregates through references
var projects = user.AssignedProjects;  // Don't do this!
var firstProjectName = projects[0].Name;  // Now User depends on Project
```

**Problems:**
- User aggregate now has direct dependency on Project aggregate
- Cannot delete Project without first updating User
- Cannot test User independently from Project
- Violates aggregate boundary principle

---

## ✅ The DDD Solution

### Core Principle

**Aggregates are isolated units that reference each other only by ID, not by object reference.**

```csharp
// ✅ CORRECT - Aggregates reference each other via IDs only
public class User
{
    // Shadow property - only IDs, not full objects
    public IList<AssignedUserToProject> AssignedProjects { get; private set; }
}

public class AssignedUserToProject
{
    // Shadow properties (UserId, ProjectId) - NOT the aggregates
    // User aggregate accessible via repository lookup, not reference
    internal User? User { get; private set; }  // EF only
    internal Project? Project { get; private set; }  // EF only
}
```

---

## 📐 Implementation Pattern

### 1. Join Entity Between Two Aggregates

**Example: User ↔ Project relationship via AssignedUserToProject**

```csharp
// Entity Definition
public class AssignedUserToProject
{
    public string? Role { get; private set; }
    public DateTime AssignedAt { get; private set; }

    // Shadow properties (managed by EF, NOT exposed):
    // private Guid UserId;
    // private Guid ProjectId;

    // Navigation properties - INTERNAL ONLY
    // Domain code cannot access aggregates through these
    internal User? User { get; private set; }
    internal Project? Project { get; private set; }

    public static AssignedUserToProject Create(string? role = null)
    {
        return new AssignedUserToProject
        {
            Role = role,
            AssignedAt = DateTime.UtcNow
        };
    }
}
```

**Why `internal`?**
- Prevents domain code from accessing full aggregates
- Forces proper repository patterns
- EF can still use them internally for relationships
- Clearly signals "EF machinery, not domain logic"

### 2. Reference Side: User Aggregate

```csharp
public class User
{
    public Email Email { get; private set; }

    // Shadow property (EF only):
    // private Guid Id;

    // ✅ Collection of join entities - NOT projects directly
    public IList<AssignedUserToProject> AssignedProjects { get; private set; } = [];

    // Child entities
    public IList<UserPermission> UserPermissions { get; private set; } = [];
    public IList<UserPreference> UserPreferences { get; private set; } = [];
}
```

### 3. Reference Side: Project Aggregate

```csharp
public class Project
{
    public ProjectShortName ShortName { get; private set; }

    // Shadow property (EF only):
    // private Guid Id;

    // ✅ Collection of join entities - NOT users directly
    public IList<AssignedUserToProject> AssignedUsers { get; private set; } = [];

    // Child entities
    public IList<ProjectTag> ProjectTags { get; private set; } = [];
}
```

### 4. EF Configuration - Enforcing Boundaries

```csharp
public class AssignedUserToProjectConfiguration : IEntityTypeConfiguration<AssignedUserToProject>
{
    public void Configure(EntityTypeBuilder<AssignedUserToProject> builder)
    {
        builder.HasKey("ProjectId", "UserId");

        // Shadow properties - EF only, not in domain
        builder.Property<Guid>("ProjectId").IsRequired();
        builder.Property<Guid>("UserId").IsRequired();

        // Relationships - for EF navigation only
        builder.HasOne(ap => ap.User)
            .WithMany(u => u.AssignedProjects)
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ap => ap.Project)
            .WithMany(p => p.AssignedUsers)
            .HasForeignKey("ProjectId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

---

## 🚀 How to Use Cross-Aggregate References

### Pattern 1: Access IDs from Join Entity (Recommended)

```csharp
// Get a user's assigned project IDs
var user = await repository.GetUserByEmail(email);

// Get ProjectIds via shadow properties (not exposed, but accessible through repo queries)
var projectIds = user.AssignedProjects
    .Select(ap => ap.ProjectId)  // Shadow property - safe to use
    .ToList();

// Get full projects separately through Project aggregate
var projects = await repository.GetProjectsByIds(projectIds);

foreach (var project in projects)
{
    Console.WriteLine(project.ShortName);
}
```

### Pattern 2: Modify Relationships Through Owner Aggregate

```csharp
// Add assignment through User aggregate
var assignment = AssignedUserToProject.Create(role: "contributor");
user.AssignedProjects.Add(assignment);

// OR add through Project aggregate
var assignment = AssignedUserToProject.Create(role: "owner");
project.AssignedUsers.Add(assignment);

// Both go through same join entity, same database row
// Modify through aggregates, never directly on join entity
```

### Pattern 3: Query Across Aggregates (Two Queries)

```csharp
// ❌ WRONG - Try to access Project through User
// var projectName = user.AssignedProjects[0].Project?.ShortName;

// ✅ CORRECT - Separate queries for separate aggregates
var assignment = user.AssignedProjects[0];
var projectId = assignment.ProjectId;  // Shadow property
var project = await repository.GetProjectById(projectId);  // Separate query
var projectName = project.ShortName;
```

---

## 📊 Cross-Aggregate Reference Inventory

### Join Entities (Two Aggregates Referenced)

| Entity | Aggregate 1 | Aggregate 2 | Pattern | Navigation Access |
|--------|------------|------------|---------|---|
| **AssignedUserToProject** | User | Project | Many-to-Many | `internal User?`, `internal Project?` |
| **UserProjectAccess** | User | Project | Many-to-Many | `internal User?`, `internal Project?` |

### Child Entities (Single Aggregate Owner)

| Entity | Parent Aggregate | Pattern | Navigation Access |
|--------|---|---|---|
| **ActionLink** | Request | One-to-Many (owned) | `internal Request?` |
| **UserPermission** | User + Project | One-to-Many (shared) | None (referenced via shadow FK) |
| **UserPreference** | User | One-to-Many (owned) | None (referenced via shadow FK) |
| **ProjectTag** | Project | One-to-Many (owned) | None (referenced via shadow FK) |
| **ProjectResource** | Provider | One-to-Many (owned) | None (referenced via shadow FK) |

### Cross-Aggregate IDs (Single Reference)

| Property | Aggregate | Referenced Aggregate | Type |
|----------|-----------|---|---|
| **User.AssignedProviderId** | User | Provider | One-to-One (via shadow property) |
| **Project.CreatedByUserId** | Project | User | One-to-One (via shadow property) |
| **Request.UserId** | Request | User | One-to-One (via shadow property) |
| **Request.ProjectId** | Request | Project | One-to-One (via shadow property) |
| **Request.ProviderId** | Request | Provider | One-to-One (via shadow property) |

---

## ❌ Anti-Patterns to Avoid

### Anti-Pattern 1: Direct Aggregate References

```csharp
// ❌ DON'T DO THIS
public class User
{
    public List<Project> Projects { get; set; }  // Direct reference to Project
}

// Why it's wrong:
// - User depends on Project (tight coupling)
// - Cannot update Project schema without affecting User queries
// - Violates aggregate boundary
```

**✅ Fix:**
```csharp
public class User
{
    public IList<AssignedUserToProject> AssignedProjects { get; private set; }
    // Shadow properties contain IDs, aggregates loaded separately
}
```

### Anti-Pattern 2: Accessing Aggregates Through Navigation

```csharp
// ❌ DON'T DO THIS
var projectName = user.AssignedProjects[0].Project?.ShortName;

// Why it's wrong:
// - Creates implicit lazy loading if navigation is public
// - Breaks aggregate isolation
// - Makes dependencies implicit rather than explicit
```

**✅ Fix:**
```csharp
var projectId = user.AssignedProjects[0].ProjectId;  // Shadow property
var project = await repository.GetProjectById(projectId);
var projectName = project.ShortName;  // Explicit, separate query
```

### Anti-Pattern 3: Modifying Join Entity Directly

```csharp
// ❌ DON'T DO THIS
var assignment = dbContext.AssignedUserToProject.First();
assignment.Role = "admin";  // Direct modification
await dbContext.SaveChangesAsync();

// Why it's wrong:
// - Bypasses aggregate patterns
// - No validation or domain logic
// - Violates business rules
```

**✅ Fix:**
```csharp
var user = await repository.GetUserByEmail(email);
var assignment = user.AssignedProjects
    .First(ap => ap.ProjectId == projectId);
// Use domain method to modify
assignment.UpdateRole("admin");
await repository.SaveAsync();
```

---

## 🔍 Boundary Enforcement Checklist

Use this checklist when creating cross-aggregate relationships:

- [ ] **Navigation Properties are `internal`**
  - Prevents domain code from accessing full aggregates through references
  - Forces explicit repository lookups

- [ ] **Shadow Properties for All IDs**
  - Foreign keys are shadow properties, not public
  - Only aggregates expose natural IDs (Email, ShortName, ContactEmail)

- [ ] **Join Entity Between Aggregates**
  - Many-to-many relationships go through join entities
  - Never direct `List<ProjectAggregate>` in User

- [ ] **IDs Used, Not Objects**
  - Domain code receives IDs, not object references
  - Load full aggregates through repository when needed

- [ ] **Separate Queries for Separate Aggregates**
  - Avoid `.Include()` to eagerly load related aggregates
  - Load each aggregate independently

- [ ] **Entity Collections Are Join/Child Entities**
  - `User.AssignedProjects` returns `IList<AssignedUserToProject>`
  - NOT `IList<Project>`

---

## 📚 Related Documentation

- **Entity Access Patterns:** `legacy-vs-refactored-entity-access-guide.md`
- **Aggregate Roots:** `entity-relationship-complete-map.md`
- **Phase 1 Analysis:** `ddd-migration-phase1-analysis.md`

---

## 💡 Key Takeaways

| Aspect | Pattern |
|--------|---------|
| **Between Aggregates** | Reference via ID (shadow property), join entity if many-to-many |
| **Navigation Properties** | `internal` - EF only, domain uses IDs |
| **Querying** | Separate queries per aggregate, no `.Include()` across boundaries |
| **Modification** | Through owner aggregate methods, never direct access |
| **Testing** | Aggregates test independently, mocking repositories instead |

---

**Remember:** Strong aggregate boundaries enable independent testing, scaling, and evolution of domain logic. A few extra queries are worth the isolation and clarity they provide.

