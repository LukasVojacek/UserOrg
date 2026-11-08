# Multitenancy Design Summary

## Overview

This document captures the architectural decisions for transforming the application to a **ownership-based multitenancy model** (not isolation-based).

**Last updated:** 2026-05-25  
**Status:** Design phase (not yet implemented)

---

## Core Model: Ownership-based Multitenancy

### What it means

```
READ    → Unrestricted (PlatformUser sees all data across all providers)
WRITE   → Owner-scoped (only ProviderAdmin can modify their provider's data)
```

**Provider = Tenant** — organizational unit that owns Locations, Projects, Resources.

### Key principle: Separation of Duties

```
PlatformAdmin          |  ProviderAdmin
───────────────────────┼──────────────────────────
Platform management    |  Data management
User/role management   |  Data creation/modification
System configuration   |  Resource governance
Audit oversight        |  Own data ReadOnly toggle
❌ Write tenant data   |  ✅ Write own tenant data
```

---

## Architecture Decision: Three Levels

### Level 1: Platform (System-wide)
- **PlatformAdmin** role — manage roles, users, system config
- Permissions: `perm:users:*`, `perm:roles:*`, `perm:system:*`
- READ: everything
- WRITE: platform infrastructure only, **not** tenant data (except via explicit admin-override)

### Level 2: Provider (Tenant)
- **ProviderAdmin** role — manage own provider's data
- Permissions: `perm:provider:{id}:data:*`, `perm:provider:{id}:location:*`, etc.
- READ: all data
- WRITE: only own provider's entities
- Can toggle `EOperationMode` on own entities

### Level 3: Project (Collaborative scope)
- **ProjectOwner**, **ProjectManager**, **ProjectMember** roles
- Same ownership boundary as provider

---

## Database Schema Change: Minimal

### Only addition needed: `Projects.ProviderId` FK

```sql
ALTER TABLE Projects ADD ProviderId uniqueidentifier NULL;

-- Backfill from existing relationship chain
UPDATE p SET p.ProviderId = (
    SELECT TOP 1 l.ProviderId
    FROM AssignedProjectResourceToProjects apr
    JOIN ProjectResources pr ON apr.ProjectResourceId = pr.Id
    JOIN AssignedLocations al ON al.ProjectResourceId = pr.Id
    JOIN Locations l ON al.LocationId = l.Id
    WHERE apr.ProjectId = p.Id AND al.IsDeleted = 0
)
FROM Projects p;
```

**Why nullable:** Some projects may have no resources initially; cross-provider projects store primary provider.

### What is NOT needed
- ❌ Global EF query filters (reads are unrestricted)
- ❌ `ITenantContext` scoped service (no automatic filtering)
- ❌ Separate schemas or databases
- ❌ Row-level security (RLS) at SQL level

---

## Enforcement Strategy: Service Layer Only

### Write Operations (CREATE, UPDATE, DELETE)

Every mutating method must verify ownership:

```csharp
private static void EnsureCallerOwnsEntity(
    Guid? entityProviderId, 
    Guid? callerProviderId)
{
    if (callerProviderId is null) 
        return;  // PlatformAdmin — bypass
    
    if (entityProviderId != callerProviderId)
        throw new ForbiddenException();
}
```

**Applies to:**
- `ProjectService` — UpdateProject, DeleteProject, AssignUserToProject, AssignResource, etc.
- `ProviderService` — UpdateProvider, DeleteProvider
- `LocationService` — UpdateLocation, DeleteLocation
- `ProjectResourceService` — UpdateProjectResource, DeleteProjectResource

### Read Operations (GET)

**No tenant filtering** — all data returned. Application logic decides what to display based on user role.

```csharp
// No change to existing queries
var allProjects = await _projectService.GetProjectsAsync();
// ProviderAdmin sees all projects in the list
// UI layer decides what to highlight as "my projects"
```

### Data Stamping on Creation

When a ProviderAdmin creates a new entity, automatically stamp it with their provider:

```csharp
public async Task<Project> InsertProjectAsync(CreateProjectRequest req, Guid? callerProviderId, ...)
{
    var project = new Project { Name = req.Name, ... };
    
    if (callerProviderId.HasValue)
        project.ProviderId = callerProviderId;  // Associate with creator's provider
    
    await _context.SaveChangesAsync();
    return project;
}
```

---

## ReadOnly Mode: Two-level Semantics

The existing `EOperationMode` (Unspecified/ReadOnly/Editable) + `ServiceName` field already encodes ownership:

```
ServiceName = "Waldur"   + Mode = ReadOnly  →  System lock (external sync)
ServiceName = "Lexis"    + Mode = ReadOnly  →  Tenant lock (admin-locked)
ServiceName = "Lexis"    + Mode = Editable  →  Explicitly unlocked
ServiceName = "Lexis"    + Mode = Unspecified  →  Normal (default)
```

### New endpoints: Toggle ReadOnly per entity

**ProviderAdmin can toggle their own data:**
```
PATCH /Project/{shortName}/mode
PATCH /Location/{id}/mode
PATCH /Provider/{id}/mode

Request body: { "mode": "ReadOnly" | "Editable" | "Unspecified" }
```

**Guard:** ProviderAdmin cannot unlock system-managed (Waldur) locks:
```csharp
if (entity.Mode == ReadOnly && entity.ServiceName != "Lexis")
    throw new RecordReadOnlyException("System-managed record");
```

### New Forge.Core permissions
```
perm:provider:lock
perm:provider-location:lock
perm:provider-project:lock
perm:provider-resource:lock
```

---

## Admin Override: Explicit and Audited

### When PlatformAdmin must modify tenant data

**Use explicit admin-override mechanism** with mandatory audit trail:

```
PUT /Location/123
X-Admin-Override: true
X-Override-Reason: "Data corruption fix, ticket #5678"
```

Implementation:
```csharp
if (callerProviderId is null && !request.AdminOverride)
    throw new ForbiddenException("Platform admins cannot modify tenant data directly");

if (callerProviderId is null && request.AdminOverride)
{
    _logger.LogWarning(
        "ADMIN_OVERRIDE: {Action} on {EntityId} ({ProviderId})",
        "Update", locationId, entity.ProviderId);
    // Proceed with modification
}
```

**Audit trail must record:**
- Who (PlatformAdmin user)
- What (which entity, which fields changed)
- When (timestamp)
- Why (reason provided)

---

## Implementation Sequencing

1. **DB Migration** — Add `Projects.ProviderId` nullable FK + backfill
2. **Entity + EF config** — Map `Project.ProviderId` navigation
3. **Service layer** — Add `EnsureOwnershipCheck` helper to mutating methods
4. **Data stamping** — `InsertProjectAsync` stamps `ProviderId` from JWT claim
5. **ReadOnly toggle** — New `SetXxxModeAsync` methods + PATCH endpoints
6. **Audit logging** — Wire admin-override cases into audit trail
7. **Forge.Core** — Register new permission keys
8. **API responses** — Add `LockedBy` field (computed from `ServiceName` when `Mode == ReadOnly`)

---

## Security Considerations

### ✅ What this model protects against

- Accidental cross-tenant writes (ownership check catches them)
- Unauthorized tenant data modifications (checked on every write)
- Platform admin careless edits (requires explicit override + audit)
- System lock overrides (Waldur/HEAppE records cannot be modified by tenant admin)

### ⚠️ What this model does NOT protect against

- SQL injection or other code-level bypasses (no DB-level RLS)
- Data exfiltration via READ (all reads are unrestricted)
- Malicious PlatformAdmin (admin-override is explicitly audited but not blocked)
- Compromised application logic (if `EnsureOwnershipCheck` is forgotten, data leaks)

### Mitigations

- **Code review checklist:** Every new write operation must include ownership check
- **Integration tests:** Test that ProviderAdmin cannot modify foreign provider's data
- **Audit logging:** All admin overrides must be logged and reviewable
- **GitHub CODEOWNERS:** Assign ownership of authorization code to specific team members

---

## Comparison: Why this approach?

| Model | Schema change | Code change | Enforcement | Risk level | Auditability |
|---|---|---|---|---|---|
| Discriminator only (Option 1) | Minimal | Medium | Service layer | Medium | Low |
| **Ownership-based (chosen)** | **Minimal** | **Small** | **Service layer** | **Low** | **High** |
| Isolation-based (Option 3) | Minimal | Large | EF global filter | High | Medium |
| Schema per tenant | Large | Large | DB-level | Very low | Medium |

---

## Common Pitfalls to Avoid

### 🔴 Pitfall 1: Forgetting ownership check

```csharp
// ❌ WRONG — no ownership verification
public async Task UpdateLocationAsync(Guid id, UpdateLocationRequest req)
{
    var location = await _repo.GetByIdAsync(id);
    location.Name = req.Name;
    await _context.SaveChangesAsync();  // Oops, anyone can edit any location!
}

// ✅ CORRECT
public async Task UpdateLocationAsync(Guid id, UpdateLocationRequest req, Guid? callerProviderId)
{
    var location = await _repo.GetByIdAsync(id);
    EnsureCallerOwnsEntity(location.ProviderId, callerProviderId);  // Guard first!
    location.Name = req.Name;
    await _context.SaveChangesAsync();
}
```

### 🔴 Pitfall 2: Assuming reads should be filtered

```csharp
// ❌ WRONG — this filters reads, but ownership model doesn't need filtering
var projects = await _repo.GetAllBySpecificationsAsync(providerId: callerProviderId);

// ✅ CORRECT — return all data, let UI decide what to display
var projects = await _repo.GetAllBySpecificationsAsync();
```

### 🔴 Pitfall 3: Bypassing permission checks in background jobs

```csharp
// ❌ WRONG — background job modifies data without ownership check
public async Task SyncDataFromWaldur()
{
    var projects = await _context.Projects.ToListAsync();
    // No ownership check! System account can modify anything
}

// ✅ CORRECT — system account (null providerId) explicitly allowed
public async Task SyncDataFromWaldur()
{
    // Set callerProviderId = null to indicate system operation
    foreach (var proj in await _context.Projects.ToListAsync())
        await UpdateProjectAsync(proj.Id, updates, callerProviderId: null);
}
```

### 🔴 Pitfall 4: Inconsistent audit logging

```csharp
// ❌ WRONG — admin override without logging
if (isAdminOverride)
{
    entity.Name = newName;
    await _context.SaveChangesAsync();
}

// ✅ CORRECT — admin override WITH audit trail
if (isAdminOverride)
{
    _auditLog.LogAdminOverride(
        action: "Update",
        entity: entity.Id,
        providerId: entity.ProviderId,
        reason: request.OverrideReason);
    
    entity.Name = newName;
    await _context.SaveChangesAsync();
}
```

---

## Testing Strategy

### Unit Tests

```csharp
[Test]
public void EnsureOwnershipCheck_SameProvider_DoesNotThrow()
{
    var entityProviderId = Guid.NewGuid();
    var callerProviderId = entityProviderId;
    
    Assert.DoesNotThrow(() => 
        EnsureCallerOwnsEntity(entityProviderId, callerProviderId));
}

[Test]
public void EnsureOwnershipCheck_DifferentProvider_Throws()
{
    var entityProviderId = Guid.NewGuid();
    var callerProviderId = Guid.NewGuid(); // Different!
    
    Assert.Throws<ForbiddenException>(() =>
        EnsureCallerOwnsEntity(entityProviderId, callerProviderId));
}

[Test]
public void EnsureOwnershipCheck_PlatformAdmin_DoesNotThrow()
{
    var entityProviderId = Guid.NewGuid();
    Guid? callerProviderId = null;  // Platform admin
    
    Assert.DoesNotThrow(() =>
        EnsureCallerOwnsEntity(entityProviderId, callerProviderId));
}
```

### Integration Tests

```csharp
[Test]
public async Task ProviderAdminCannotModifyForeignProvider()
{
    // ProviderAdmin A tries to update Location belonging to Provider B
    var response = await PutAsync(
        "/Location/foreign-location-id",
        updateRequest,
        bearerToken: providerAdminAToken);
    
    Assert.AreEqual(403, response.StatusCode);
}

[Test]
public async Task ProviderAdminCanModifyOwnProvider()
{
    // ProviderAdmin A updates Location in Provider A
    var response = await PutAsync(
        "/Location/own-location-id",
        updateRequest,
        bearerToken: providerAdminAToken);
    
    Assert.AreEqual(200, response.StatusCode);
}
```

---

## Future Considerations

### If requirements change to isolation-based

If in the future you need to restrict **reading** by tenant (data hiding), the migration path is:

1. Add EF `HasQueryFilter()` for each tenant-scoped entity
2. Introduce `ITenantContext` scoped service
3. Update all GET endpoints to apply the filter
4. No schema changes needed (ProviderId already exists)

This is why **ownership-based is a good first step** — it can be extended to isolation later.

### Audit system

Current implementation records admin overrides in app logs. Consider:
- [ ] Structured audit table (`AuditLog` entity)
- [ ] Separate immutable audit DB
- [ ] Integration with external SIEM (Splunk, ELK)

### RBAC evolution

Current Forge.Core structure supports future refinements:
- Per-entity permissions: `perm:provider:{id}:project:{projectId}:write`
- Time-bounded permissions (temporary access)
- Delegation (ProviderAdmin delegates to ProjectOwner)

---

## Team Notes

- **Code review checklist item:** All write operations must include ownership verification
- **Permission:** Team must understand the difference between "what you can do" (write) vs "what you can see" (read)
- **Waldur integration:** External sync runs as system account (null providerId), always bypasses checks
- **Support runbooks:** Admin overrides should be logged; support team should document reason for every override

---

## Questions for Future Implementation

1. Should cross-provider projects be allowed, or should a project belong to exactly one provider?
2. Should `Projects.ProviderId` be required (NOT NULL) after backfill, or stay nullable?
3. Should PlatformAdmin be able to transfer a project between providers (reparent)?
4. How long should audit logs be retained?
5. Should there be a "data ownership review" workflow for compliance?

