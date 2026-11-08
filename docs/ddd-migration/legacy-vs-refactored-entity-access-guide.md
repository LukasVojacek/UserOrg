# Legacy vs. Refactored: Entity Access Pattern Guide

**Status:** Reference Documentation  
**Purpose:** Comprehensive guide to understanding how entities are accessed in Legacy vs. DDD Refactored patterns  
**Created:** 2026-04-28  
**Audience:** Developers, API consumers, architects

---

## 🎯 Quick Navigation

- [Aggregate Roots Overview](#-aggregate-roots-overview) - 4 main domain aggregates
- [Entity Access by Category](#-entity-access-by-category) - Legacy vs. Refactored for each entity
- [API Endpoint Migration](#-api-endpoint-migration) - How endpoints change
- [Child Entity Access Patterns](#-child-entity-access-patterns) - How to access nested entities

---

## 📦 Aggregate Roots Overview

### The 4 Domain Aggregates

Each aggregate root is the entry point for accessing its child entities. You **cannot** access child entities directly; you must go through their parent aggregate.

```
┌─────────────────────────────────────────────────────────────────┐
│                     DOMAIN AGGREGATES (4)                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  1️⃣ USER               2️⃣ PROJECT          3️⃣ PROVIDER         │
│  └─ Email (Natural ID) └─ ShortName        └─ Name              │
│     (Unique)              (Unique)           (Unique)           │
│                                                                   │
│  4️⃣ REQUEST                                                      │
│  └─ Slug  req-{e3}-{shortname}-{r6}  (ADR-0024)                 │
│     (GUID-free public ref; no natural ID)                       │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 📋 Entity Access by Category

### User Aggregate

#### Aggregate Root: User

| Aspect | Legacy Approach | Refactored DDD Approach |
|--------|---|---|
| **Natural ID** | UUID (auto-generated Guid) | **Email** (business-meaningful) |
| **DB Access** | `WHERE [Id] = 550e8400-...` | `WHERE [Email] = 'user@company.com'` |
| **API V1** | `GET /api/users/{userId}` | `GET /api/users/{userId}` (unchanged) |
| **API V2** | ❌ Not available | `GET /api/v2/Users/{email}` |
| **Response** | `{ "id": "550e8400-...", "email": "..." }` | `{ "email": "user@company.com", ... }` (no public id) |
| **Query** | Repository lookup by UUID | Repository lookup by Email (natural key) |
| **Immutability** | Public Id property | Shadow property (EF-only, hidden from code) |
| **Who accesses** | Direct via Id | Direct via Email or JWT token |

**Domain Model (Refactored):**
```csharp
public class User
{
    // Shadow property (not exposed):
    // - Id (Guid, PK) ← Hidden from domain code
    
    public Email Email { get; private set; }  // ← Natural Identifier
    public string UserName { get; private set; }
    public IList<UserPermission> UserPermissions { get; private set; }
    public IList<UserPreference> UserPreferences { get; private set; }
    public IList<AssignedUserToProject> AssignedProjects { get; private set; }
}
```

**Child Entities under User:** 3 entities
- **UserPermission** - Permissions assigned to user for specific projects
  - *Access:* Only through User aggregate → `User.UserPermissions`
  - *Legacy:* `GET /api/userPermissions/{permissionId}` (direct access)
  - *Refactored:* `GET /api/v2/Users/{email}` → contains permissions in response
  
- **UserPreference** - User settings (notifications, theme, system module settings)
  - *Access:* Only through User aggregate → `User.UserPreferences`
  - *Legacy:* `GET /api/userPreferences/{preferenceId}` (direct access)
  - *Refactored:* Access via User aggregate only, modify through User aggregate methods

- **AssignedUserToProject** - User assignment to projects with role information
  - *Access:* Through User or Project aggregate → `User.AssignedProjects` or `Project.AssignedUsers`
  - *Legacy:* `GET /api/assignments/{id}` (direct query possible)
  - *Refactored:* Must load through either User or Project aggregate

---

### Project Aggregate

#### Aggregate Root: Project

| Aspect | Legacy Approach | Refactored DDD Approach |
|--------|---|---|
| **Natural ID** | UUID (auto-generated Guid) | **ProjectShortName** (business-meaningful, e.g., "LEX", "LEXIS") |
| **DB Access** | `WHERE [Id] = 550e8400-...` | `WHERE [ShortName] = 'LEX'` |
| **API V1** | `GET /api/projects/{projectId}` | `GET /api/projects/{projectId}` (unchanged) |
| **API V2** | ❌ Not available | `GET /api/v2/Projects/{shortName}` |
| **Response** | `{ "id": "550e8400-...", "shortName": "LEX", ... }` | `{ "shortName": "LEX", ... }` (no public id) |
| **Query** | Repository lookup by UUID | Repository lookup by ShortName (natural key) |
| **Child Entities** | Some directly queryable | **ALL hidden** - access only through Project aggregate |
| **Immutability** | Public Id property | Shadow property (EF-only, hidden from code) |

**Domain Model (Refactored):**
```csharp
public class Project
{
    // Shadow property (not exposed):
    // - Id (Guid, PK) ← Hidden from domain code
    
    public ProjectShortName ShortName { get; private set; }  // ← Natural Identifier
    public ProjectName Name { get; private set; }
    public string? Description { get; private set; }
    public Email? Email { get; private set; }
    public int ProjectDomainTypeId { get; private set; }  // FK to reference
    public UserId CreatedByUserId { get; private set; }   // Cross-aggregate FK
    
    public IList<ProjectTag> ProjectTags { get; private set; }
    public IList<UserPermission> UserPermissions { get; private set; }
    public IList<AssignedUserToProject> AssignedUsers { get; private set; }
    public IList<AssignedProjectResourceToProject> AssignedResources { get; private set; }
}
```

**Child Entities under Project:** 4 entities
- **ProjectTag** - Tags for categorizing projects
  - *Access:* `Project.ProjectTags` only
  - *Legacy:* `GET /api/projectTags/{tagId}` (direct, independent access)
  - *Refactored:* ⛔ No direct endpoint; access through Project: `GET /api/v2/Projects/{shortName}` → `tags[]`

- **UserPermission** - Permissions for user within specific project
  - *Access:* `Project.UserPermissions` (same entity used by User aggregate)
  - *Legacy:* `GET /api/permissions/{id}` (direct access)
  - *Refactored:* Access through Project or User aggregate only

- **AssignedUserToProject** - Assignment of users to project with roles
  - *Access:* `Project.AssignedUsers` or `User.AssignedProjects`
  - *Legacy:* `GET /api/assignments/{id}` (direct queryable)
  - *Refactored:* Access only through User or Project aggregate

- **AssignedProjectResourceToProject** - Allocation of resources to project
  - *Access:* `Project.AssignedResources` only
  - *Legacy:* `GET /api/projectResources/{resourceId}` (direct)
  - *Refactored:* ⛔ No independent endpoint; access through Project aggregate only

---

### Provider Aggregate

#### Aggregate Root: Provider

| Aspect | Legacy Approach | Refactored DDD Approach |
|--------|---|---|
| **Natural ID** | UUID (auto-generated Guid) | **Name** (provider name, e.g., "XSEDE", "PRACE") |
| **DB Access** | `WHERE [Id] = 550e8400-...` | `WHERE [Name] = 'XSEDE'` |
| **API V1** | `GET /api/providers/{providerId}` | `GET /api/providers/{providerId}` (unchanged) |
| **API V2** | ❌ Not available | `GET /api/v2/Providers/{name}` |
| **Response** | `{ "id": "550e8400-...", "name": "XSEDE", ... }` | `{ "name": "XSEDE", ... }` (no public id) |
| **Query** | Repository lookup by UUID | Repository lookup by Name (natural key) |
| **Child Access** | Some directly queryable | **ALL hidden** - access through Provider aggregate |
| **Immutability** | Public Id property | Shadow property (EF-only, hidden from code) |

**Domain Model (Refactored):**
```csharp
public class Provider
{
    // Shadow property (not exposed):
    // - Id (Guid, PK) ← Hidden from domain code
    
    public ProviderName Name { get; private set; }  // ← Natural Identifier
    public string? Description { get; private set; }
    public Email? SupportEmail { get; private set; }
    public int? CountryId { get; private set; }  // FK to reference Country
    public bool IsPrivate { get; private set; }
    
    public IList<ProjectResource> ProjectResources { get; private set; }
    public IList<Location> Locations { get; private set; }
}
```

**Child Entities under Provider:** 2 direct children

- **ProjectResource** - HPC/storage resources offered by provider
  - *Access:* `Provider.ProjectResources` only
  - *Legacy:* `GET /api/projectResources/{resourceId}` (direct access)
  - *Refactored:* ⛔ No independent endpoint; access through Provider: `GET /api/v2/Providers/{name}` → `resources[]`

- **Location** - Physical or logical locations where provider operates
  - *Access:* `Provider.Locations` only
  - *Legacy:* `GET /api/locations/{locationId}` (direct access)
  - *Refactored:* ⛔ No independent endpoint; access through Provider: `GET /api/v2/Providers/{name}` → `locations[]`

---

### Request Aggregate

#### Aggregate Root: Request

| Aspect | Legacy Approach | Refactored DDD Approach |
|--------|---|---|
| **Natural ID** | UUID (auto-generated Guid) | **None** — a generated GUID-free **slug** `req-{e3}-{shortname}-{r6}` is the public ref (ADR-0024). ContactEmail is a data field, not a key (not unique). |
| **DB Access** | `WHERE [Id] = 550e8400-...` | `WHERE [Slug] = 'req-...'` (surrogate `requestId` GUID stays as the hidden PK) |
| **API V1** | `GET /api/requests/{requestId}` | `GET /api/requests/{requestId}` (unchanged) |
| **API V2** | ❌ Not available | write path is **self-onboarding only** (ADR-0009): `POST /api/v2/Requests` → `{Slug}`; `PUT …/requests/{slug}/accept`·`/reject`; anonymous read `GET /api/v2/Public/Requests/ByLink/{actionLink}` |
| **Response** | `{ "id": "550e8400-...", "contactEmail": "...", ... }` | `{ "slug": "req-...", ... }` (no GUID exposed — ADR-0024) |
| **Query** | Repository lookup by UUID | Lookup by slug (v2-created rows only; v1 null-slug rows → 404) |
| **Child Access** | ActionLinks directly queryable | **Hidden** - access through Request aggregate |
| **Immutability** | Public Id property | Shadow property (EF-only, hidden from code) |

**Domain Model (Refactored):**
```csharp
public class Request
{
    // Shadow property (not exposed):
    // - Id (Guid, PK) ← Hidden from domain code
    
    public Email ContactEmail { get; private set; }  // ← Natural Identifier
    public int Type { get; private set; }  // RequestType enum
    public int State { get; private set; }  // RequestState enum
    public ProjectId ProjectId { get; private set; }  // Cross-aggregate FK
    public UserId UserId { get; private set; }  // Cross-aggregate FK
    public ProjectResourceId? ProjectResourceId { get; private set; }  // Cross-aggregate FK
    public string? ProjectResourceName { get; private set; }
    public ProviderId? ProviderId { get; private set; }  // Cross-aggregate FK
    
    public IList<ActionLink> ActionLinks { get; private set; }
}
```

**Child Entities under Request:** 1 direct child

- **ActionLink** - Approval/rejection links with tokens for requests
  - *Access:* `Request.ActionLinks` only
  - *Legacy:* `GET /api/actionLinks/{linkId}` (direct access)
  - *Refactored:* ⛔ No independent endpoint; the action-link token itself resolves the request anonymously: `GET /api/v2/Public/Requests/ByLink/{actionLink}` (ADR-0024)

---

## 🔗 Child Entity Access Patterns

### How Child Entities Are Accessed

#### Legacy Pattern (❌ Old Way)
```
Direct Query by Child Entity ID
GET /api/userPermissions/550e8400-...          ← Direct child access
GET /api/projectTags/550e8400-...              ← Direct child access
GET /api/actionLinks/550e8400-...              ← Direct child access
GET /api/assignments/550e8400-...              ← Direct child access
```

**Problem:** Child entities exposed as separate resources, breaking aggregate boundaries. You could modify a child without going through its parent, violating domain invariants.

#### Refactored DDD Pattern (✅ New Way)
```
Access Through Parent Aggregate Only
GET /api/v2/Users/{email}
{
    "email": "user@company.com",
    "permissions": [
        { "name": "admin", "description": "..." },
        { "name": "viewer", "description": "..." }
    ]
}

GET /api/v2/Projects/{shortName}
{
    "shortName": "LEX",
    "name": "LEXIS Project",
    "tags": ["hpc", "research"],
    "users": [
        { "email": "user1@org.com", "role": "owner" },
        { "email": "user2@org.com", "role": "contributor" }
    ]
}
```

**Benefit:** All modifications flow through parent aggregate, ensuring business rules are enforced.

---

## 📊 API Endpoint Migration

### User Endpoints

| Endpoint | Legacy (V1) | Refactored (V2) | Access Pattern | Status |
|----------|---|---|---|---|
| **Get user** | `GET /api/users/{userId}` | `GET /api/v2/Users/{email}` | Direct via natural ID | ⚠️ Breaking |
| **List users** | `GET /api/users` | `GET /api/v2/Users` | List (no filter) | ✅ Same |
| **Create user** | `POST /api/users` | `POST /api/v2/Users` | Same | ✅ Same |
| **Update user** | `PUT /api/users/{userId}` | `PUT /api/v2/Users/{email}` | Natural ID route | ⚠️ Breaking |
| **Get permissions** | `GET /api/userPermissions?userId={...}` | ❌ Removed | Via User aggregate | ❌ Deprecated |
| **Get preferences** | `GET /api/userPreferences?userId={...}` | ❌ Removed | Via User aggregate | ❌ Deprecated |

### Project Endpoints

| Endpoint | Legacy (V1) | Refactored (V2) | Access Pattern | Status |
|----------|---|---|---|---|
| **Get project** | `GET /api/projects/{projectId}` | `GET /api/v2/Projects/{shortName}` | Direct via natural ID | ⚠️ Breaking |
| **List projects** | `GET /api/projects` | `GET /api/v2/Projects` | List (no filter) | ✅ Same |
| **Create project** | `POST /api/projects` | `POST /api/v2/Projects` | Same | ✅ Same |
| **Update project** | `PUT /api/projects/{projectId}` | `PUT /api/v2/Projects/{shortName}` | Natural ID route | ⚠️ Breaking |
| **Get tags** | `GET /api/projectTags?projectId={...}` | ❌ Removed | Via Project aggregate | ❌ Deprecated |
| **Add tag** | `POST /api/projectTags` | ❌ Removed | Via Project aggregate method | ❌ Deprecated |
| **Delete tag** | `DELETE /api/projectTags/{tagId}` | ❌ Removed | Via Project aggregate method | ❌ Deprecated |

### Provider Endpoints

| Endpoint | Legacy (V1) | Refactored (V2) | Access Pattern | Status |
|----------|---|---|---|---|
| **Get provider** | `GET /api/providers/{providerId}` | `GET /api/v2/Providers/{name}` | Direct via natural ID | ⚠️ Breaking |
| **List providers** | `GET /api/providers` | `GET /api/v2/Providers` | List (no filter) | ✅ Same |
| **Create provider** | `POST /api/providers` | `POST /api/v2/Providers` | Same | ✅ Same |
| **Update provider** | `PUT /api/providers/{providerId}` | `PUT /api/v2/Providers/{name}` | Natural ID route | ⚠️ Breaking |
| **Get resources** | `GET /api/projectResources?providerId={...}` | ❌ Removed | Via Provider aggregate | ❌ Deprecated |
| **Get locations** | `GET /api/locations?providerId={...}` | ❌ Removed | Via Provider aggregate | ❌ Deprecated |

### Request Endpoints

| Endpoint | Legacy (V1) | Refactored (V2) | Access Pattern | Status |
|----------|---|---|---|---|
| **Get request (public)** | `GET /api/Public/RequestInfo/{requestId}` | `GET /api/v2/Public/Requests/ByLink/{actionLink}` | Anonymous, by single-use action-link token | ⚠️ Breaking (no GUID — ADR-0024) |
| **List requests** | `GET /api/requests` | `GET /api/v2/Requests` (caller-scoped) + `…/requests/sent` | List | ✅ Same route |
| **Create request** | `POST /api/requests` | `POST /api/v2/Requests` → `{Slug}` | Self-onboarding only (ADR-0009) | ⚠️ Breaking |
| **Accept / reject** | `PUT /api/requests/{requestId}/Accept`·`/Reject` | `PUT /api/v2/Requests/{slug}/Accept`·`/reject` | By slug, project-scoped | ⚠️ Breaking |
| **Get action links** | `GET /api/actionLinks?requestId={...}` | ❌ Removed | Via Request aggregate | ❌ Deprecated |

---

## 🚀 Migration Guide: From Legacy to Refactored

### Step 1: Update Query Code

**BEFORE (Legacy):**
```csharp
var user = await dbContext.Users
    .FirstOrDefaultAsync(u => u.Id == userId);

var project = await dbContext.Projects
    .FirstOrDefaultAsync(p => p.Id == projectId);
```

**AFTER (Refactored):**
```csharp
var user = await dbContext.Users
    .FirstOrDefaultAsync(u => u.Email == Email.From("user@company.com"));

var project = await dbContext.Projects
    .FirstOrDefaultAsync(p => p.ShortName == ProjectShortName.Create("LEX"));
```

### Step 2: Update API Calls

**BEFORE (Legacy Client Code):**
```csharp
var response = await httpClient.GetAsync($"/api/users/{userId}");
var user = await response.Content.ReadFromJsonAsync<UserDto>();
// user.Id is available

var response = await httpClient.GetAsync($"/api/projects/{projectId}");
var project = await response.Content.ReadFromJsonAsync<ProjectDto>();
// project.Id is available
```

**AFTER (Refactored Client Code):**
```csharp
var response = await httpClient.GetAsync($"/api/v2/Users/user@company.com");
var user = await response.Content.ReadFromJsonAsync<UserV2Dto>();
// user.Id is NOT available (use Email as identifier)

var response = await httpClient.GetAsync($"/api/v2/Projects/LEX");
var project = await response.Content.ReadFromJsonAsync<ProjectV2Dto>();
// project.Id is NOT available (use ShortName as identifier)
```

### Step 3: Access Child Entities

**BEFORE (Legacy - Direct Access):**
```csharp
var tags = await dbContext.ProjectTags
    .Where(t => t.ProjectId == projectId)
    .ToListAsync();

var permissions = await dbContext.UserPermissions
    .Where(p => p.UserId == userId)
    .ToListAsync();
```

**AFTER (Refactored - Through Aggregate):**
```csharp
var project = await dbContext.Projects
    .Include(p => p.ProjectTags)
    .FirstOrDefaultAsync(p => p.ShortName == shortName);
// var tags = project.ProjectTags;

var user = await dbContext.Users
    .Include(u => u.UserPermissions)
    .FirstOrDefaultAsync(u => u.Email == email);
// var permissions = user.UserPermissions;
```

---

## 📐 Complete Aggregate Structure Reference

### User Aggregate
```
USER (Aggregate Root)
├─ Natural ID: Email
├─ Shadow Property: Id (Guid, hidden)
│
└─ Child Entities (3)
   ├─ UserPermission
   │  ├─ Shadow: Id (Guid)
   │  ├─ Shadow: UserId (Guid, FK)
   │  └─ Properties: PermissionName, Description, AssignedAt
   │
   ├─ UserPreference
   │  ├─ Shadow: Id (Guid)
   │  ├─ Shadow: UserId (Guid, FK)
   │  └─ Properties: PreferenceKey, PreferenceValue
   │
   └─ AssignedUserToProject
      ├─ Shadow: Id (Guid)
      ├─ Shadow: UserId (Guid, FK)
      ├─ Shadow: ProjectId (Guid, FK)
      └─ Properties: Role, AssignedAt
```

### Project Aggregate
```
PROJECT (Aggregate Root)
├─ Natural ID: ShortName
├─ Shadow Property: Id (Guid, hidden)
│
└─ Child Entities (4)
   ├─ ProjectTag
   │  ├─ Shadow: Id (Guid)
   │  ├─ Shadow: ProjectId (Guid, FK)
   │  └─ Properties: TagName, CreatedAt
   │
   ├─ UserPermission (shared with User)
   │  ├─ Shadow: Id (Guid)
   │  ├─ Shadow: ProjectId (Guid, FK)
   │  └─ Properties: PermissionName, Description
   │
   ├─ AssignedUserToProject (shared with User)
   │  ├─ Shadow: Id (Guid)
   │  ├─ Shadow: ProjectId (Guid, FK)
   │  └─ Properties: Role, AssignedAt
   │
   └─ AssignedProjectResourceToProject
      ├─ Shadow: Id (Guid)
      ├─ Shadow: ProjectId (Guid, FK)
      ├─ Shadow: ProjectResourceId (Guid, FK)
      └─ Properties: Order, CreatedAt
```

### Provider Aggregate
```
PROVIDER (Aggregate Root)
├─ Natural ID: Name
├─ Shadow Property: Id (Guid, hidden)
│
└─ Child Entities (2)
   ├─ ProjectResource
   │  ├─ Shadow: Id (Guid)
   │  ├─ Shadow: ProviderId (Guid, FK)
   │  └─ Properties: ResourceName, ResourceType
   │
   └─ Location
      ├─ Shadow: Id (Guid)
      ├─ Shadow: ProviderId (Guid, FK)
      └─ Properties: LocationName, Address, City, Country
```

### Request Aggregate
```
REQUEST (Aggregate Root)
├─ Natural ID: ContactEmail
├─ Shadow Property: Id (Guid, hidden)
│
└─ Child Entities (1)
   └─ ActionLink
      ├─ Shadow: Id (Guid)
      ├─ Shadow: RequestId (Guid, FK)
      └─ Properties: Action, Link, ValidTo, CreatedAt
```

---

## 🔑 Key Takeaways

| Aspect | Legacy | Refactored DDD |
|--------|--------|---|
| **Entity Access** | Direct by UUID | Through aggregate by natural ID |
| **Child Access** | Direct queries possible | Only through parent aggregate |
| **API Contracts** | V1: `/api/users/{id}` | V2: `/api/v2/Users/{email}` |
| **Response IDs** | Public `id` in response | No public ID (use natural identifier) |
| **Query Pattern** | `WHERE Id = ...` | `WHERE NaturalKey = ...` |
| **Business Logic** | Scattered | Centralized in aggregates |
| **Invariants** | Easy to violate | Enforced by aggregate |
| **API Versioning** | Single version | V1 (UUID) + V2 (Natural ID) |

---

## 📚 Related Documentation

- **Phase 1 Analysis:** `ddd-migration-phase1-analysis.md` - Aggregate root definitions
- **Entity Map:** `entity-relationship-complete-map.md` - Complete entity relationship graph
- **API Versioning:** `api-versioning-gateway-routing.md` - Gateway routing strategy
- **Phase 3 Implementation:** `ddd-migration-phase3-entities.md` - Shadow property implementation

