# Phase 1: Architecture & Pattern Analysis (Implementation Guide)

**Status:** IN PROGRESS  
**Objective:** Identify all Aggregate Roots, map relationships, document natural identifiers, and analyze API endpoints  
**Deliverables:** Detailed architecture documentation, entity mapping matrix, natural identifier inventory

---

## 1.1 Aggregate Root Identification

### Current Analysis Results

Based on codebase analysis, the following entities should be **Aggregate Roots**:

#### ✅ User Aggregate Root
```
┌─ User (Aggregate Root)
│  ├─ Email (Natural Identifier - UNIQUE)
│  ├─ UserPermission (Child Entity)
│  ├─ UserPreference (Child Entity)
│  └─ AssignedUserToProject (Child Entity)
```

**Why User is an Aggregate Root:**
- Represents a distinct user identity
- Has its own lifecycle (create, activate, deactivate)
- Owns permissions, preferences, and project assignments
- Queried by Email (natural identifier)
- Cannot exist without owning at least default permission
- Responsible for managing user-project relationships

**Natural Identifier:** `Email` (string, unique, immutable)

**Child Entities:**
- `UserPermission` - Permissions assigned to user for specific projects
- `UserPreference` - User preferences (notifications, theme, system module settings)
- `AssignedUserToProject` - Project assignments with role information

**Current DB Schema:**
```sql
CREATE TABLE [User] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [Email] NVARCHAR(255) UNIQUE NOT NULL,
    [FirstName] NVARCHAR(100),
    [LastName] NVARCHAR(100),
    [CreatedAt] DATETIME,
    [ModifiedAt] DATETIME,
    [IsDeleted] BIT
);

CREATE TABLE [UserPermission] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionName] NVARCHAR(100),
    FOREIGN KEY ([UserId]) REFERENCES [User]([Id])
);

CREATE TABLE [UserPreference] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [PreferenceName] NVARCHAR(100),
    [PreferenceValue] NVARCHAR(MAX),
    FOREIGN KEY ([UserId]) REFERENCES [User]([Id])
);
```

---

#### ✅ Project Aggregate Root
```
┌─ Project (Aggregate Root)
│  ├─ ProjectShortName (Natural Identifier - UNIQUE)
│  ├─ ProjectTag (Child Entity)
│  ├─ AssignedUserToProject (Child Entity)
│  ├─ AssignedProjectResourceToProject (Child Entity)
│  ├─ ProjectDomainType (Reference)
│  └─ UserPermission (Child Entity)
```

**Why Project is an Aggregate Root:**
- Represents a distinct project entity
- Has its own lifecycle
- Owns tags, user assignments, resource assignments, and permissions
- Queried by ProjectShortName (natural identifier)
- Business rules specific to projects (active date checks, user limits)
- Central entity that manages all project-related relationships

**Natural Identifier:** `ProjectShortName` (value object, unique, semantic)

**Child Entities:**
- `ProjectTag` - Tags associated with project
- `AssignedUserToProject` - Users assigned to project with roles and audit info
- `AssignedProjectResourceToProject` - Resources (HPC, storage) assigned to project
- `UserPermission` - Permissions granted for this project
- `ProjectDomainType` (Reference) - Type/domain classification of project

**Current DB Schema:**
```sql
CREATE TABLE [Project] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [ShortName] NVARCHAR(50) UNIQUE NOT NULL,
    [Name] NVARCHAR(255),
    [Description] NVARCHAR(MAX),
    [StartDate] DATETIME,
    [EndDate] DATETIME,
    [CreatedAt] DATETIME,
    [ModifiedAt] DATETIME,
    [IsDeleted] BIT
);

CREATE TABLE [ProjectTag] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [ProjectId] UNIQUEIDENTIFIER NOT NULL,
    [TagName] NVARCHAR(100),
    FOREIGN KEY ([ProjectId]) REFERENCES [Project]([Id])
);

CREATE TABLE [AssignedUserToProject] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ProjectId] UNIQUEIDENTIFIER NOT NULL,
    [Role] NVARCHAR(50),
    UNIQUE ([UserId], [ProjectId])
);

CREATE TABLE [AssignedProjectResourceToProject] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [ProjectId] UNIQUEIDENTIFIER NOT NULL,
    [ProjectResourceId] UNIQUEIDENTIFIER NOT NULL,
    FOREIGN KEY ([ProjectId]) REFERENCES [Project]([Id])
);
```

---

#### ✅ Provider Aggregate Root
```
┌─ Provider (Aggregate Root)
│  ├─ Name (Natural Identifier - TBD if unique)
│  ├─ Country (Reference)
│  ├─ ProjectResource (Child Entity)
│  ├─ Location (Child Entity)
│  └─ ResponsibleUsers (Child Reference)
```

**Why Provider is an Aggregate Root:**
- Represents distinct external provider/service (HPC center, storage provider)
- Owns resources, locations, and manages responsible users
- Has separate lifecycle
- Manages location configurations and allocation units
- Owns all project resources that come from this provider

**Natural Identifier:** `Name` (if unique) or composite based on unique constraint analysis
- **TODO:** Verify if `Name` is unique in database. If not, may need to add unique constraint.

**Child Entities:**
- `ProjectResource` - HPC/storage resources provided by this provider
- `Location` - Physical or logical locations for this provider
- `Country` (Reference) - Country where provider is based
- `ResponsibleUsers` - Users responsible for managing this provider

**Current DB Schema:**
```sql
CREATE TABLE [Provider] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [Name] NVARCHAR(255),
    [Description] NVARCHAR(MAX),
    [CreatedAt] DATETIME,
    [ModifiedAt] DATETIME,
    [IsDeleted] BIT
);

CREATE TABLE [ProjectResource] (
    [Id] UNIQUEIDENTIFIER PRIMARY KEY,
    [ProviderId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(255),
    FOREIGN KEY ([ProviderId]) REFERENCES [Provider]([Id])
);
```

---

#### ✅ Request (SelfOnboarding) Aggregate Root
```
┌─ Request (Aggregate Root)
│  ├─ State (Status: Pending, Approved, Rejected)
│  ├─ Type (Request type)
│  ├─ User (Reference)
│  ├─ Project (Reference)
│  ├─ Provider (Reference)
│  ├─ ProjectResource (Reference)
│  └─ ActionLink (Child Entity)
```

**Why Request is an Aggregate Root:**
- Represents distinct self-onboarding/resource request
- Has its own state machine (Pending, Approved, Rejected)
- Independent lifecycle managed by approval workflow
- Manages action links (approval/rejection links sent to users)
- Coordinates between user, project, provider, and resources

**Natural Identifier:** ID (UUID) or composite (User+Type) - **TODO:** Verify if there's a better natural identifier
- **RECOMMENDATION:** Use composite natural ID: `(ContactEmail, Type, State)` or email-based

**Child Entities:**
- `ActionLink` - Approval/rejection links for this request

**Related Entities (References):**
- `User` - User making the request
- `Project` - Project being requested for
- `Provider` - Provider involved in request
- `ProjectResource` - Specific resource being requested

**Potential Improvements:**
- Add `ContactEmail` field as natural identifier component
- Document state transitions and validation rules

---

### 1.2 Shared/Reference Entities (NOT Aggregate Roots)

These entities are **NOT** aggregate roots. They are reference data, value objects, or child entities of aggregate roots:

| Entity | Type | Parent/Access | Purpose |
|--------|------|---|---|
| AllocationUnitType | Enum Reference | Reference Data | Lookup table (e.g., CPU, Memory, Storage units) |
| Country | Enum Reference | Reference Data | Country catalog for providers |
| LocationType | Enum Reference | Reference Data | Lookup table (e.g., Physical, Virtual) |
| ConfigurationType | Enum Reference | Reference Data | Configuration type classification |
| ProjectDomainType | Enum Reference | Reference Data | Project domain classification |
| SystemModule | Enum Reference | Reference Data | System modules for preferences |
| Configuration | Reference | Direct query | System configuration storage |
| ConfigurationValue | Child Entity | Location via ProjectResource | Configuration values for locations |
| UserProjectAccess | Join Table | Via aggregates | Access control mapping (User↔Project) |
| Location | Complex Reference | Provider (aggregate) | Physical/logical locations with endpoints |
| AssignedLocation | Child Entity | Location/ProjectResource | Resource allocation at specific location |
| AssignedLocationValue | Child Entity | AssignedLocation | Values/metrics for allocation |
| ActionLink | Child Entity | Request (aggregate) | Action links (approve/reject) for requests |

---

## 1.2.1 Detailed Child Entity & Reference Entity Breakdown

### Location Hierarchy
```
Location (Reference Entity - accessed via Provider)
├── LocationType (Enum Reference)
├── AssignedLocation (Child Entity - accessed via ProjectResource)
│   ├── ProjectResource (Reference to aggregate)
│   ├── AllocationUnitType (Enum Reference)
│   └── AssignedLocationValue (Child Entity - leaf node)
│       ├── Name (metadata)
│       └── Value (metric value)
└── ConfigurationValue (Child Entity - configuration for this location)
    ├── Configuration (Reference)
    └── ConfigurationType (Enum Reference)
```

**Key Insight:** AssignedLocation and AssignedLocationValue form a tree structure representing how a ProjectResource is allocated across locations with specific unit counts and metrics.

### Request Hierarchy
```
Request (Aggregate Root)
├── User (Reference to User aggregate)
├── Project (Reference to Project aggregate)
├── Provider (Reference to Provider aggregate)
├── ProjectResource (Reference to Provider aggregate child)
└── ActionLink (Child Entity)
    ├── EActionType (Enum)
    └── ValidTo (expiration tracking)
```

**Key Insight:** Requests coordinate approval workflows using ActionLinks as approval tokens.

---

## 1.3 Entity-to-Aggregate Root Assignment Matrix

```
AGGREGATE ROOTS (4)
├── User
│   ├── Email (natural ID)
│   ├── USER PERMISSION (child)
│   ├── USER PREFERENCE (child)
│   └── ASSIGNED USER TO PROJECT (child)
├── Project
│   ├── ProjectShortName (natural ID)
│   ├── PROJECT TAG (child)
│   ├── USER PERMISSION (child)
│   ├── ASSIGNED USER TO PROJECT (child)
│   ├── ASSIGNED PROJECT RESOURCE TO PROJECT (child)
│   └── PROJECT DOMAIN TYPE (reference)
├── Provider
│   ├── Name (natural ID - TBD)
│   ├── COUNTRY (reference)
│   ├── PROJECT RESOURCE (child)
│   ├── LOCATION (child)
│   └── RESPONSIBLE USERS (reference)
└── Request
    ├── ContactEmail / UUID (natural ID - TBD)
    ├── USER (reference)
    ├── PROJECT (reference)
    ├── PROVIDER (reference)
    ├── PROJECT RESOURCE (reference)
    └── ACTION LINK (child)

SHARED/REFERENCE ENTITIES (10+)
├── AllocationUnitType
├── Country
├── Location (+ AssignedLocation, AssignedLocationValue children)
├── LocationType
├── Configuration
├── ConfigurationType
├── ConfigurationValue
├── SystemModule
├── ProjectDomainType
├── UserProjectAccess (join table for access control)
├── AssignedLocation (child of Location via ProjectResource)
├── AssignedLocationValue (child of AssignedLocation)
└── ActionLink (child of Request)
```

---

## 1.4 Natural Identifier Inventory

### Confirmed Natural Identifiers

| Aggregate Root | Natural Identifier | Type | Implementation | Uniqueness |
|---|---|---|---|---|
| **User** | Email | string | Already has `Email` property | ✅ UNIQUE constraint exists |
| **Project** | ProjectShortName | ProjectShortName (value object) | Already has `ShortName` property | ✅ UNIQUE constraint exists |
| **Provider** | Name | string | Currently just `Name` | ⚠️ **TO VERIFY** |
| **Request** | TBD | TBD | Need analysis | ❓ Needs investigation |

### Natural Identifier Verification Queries

**To verify natural identifier uniqueness in database:**

```sql
-- User natural ID
SELECT Email, COUNT(*) as cnt FROM [User] GROUP BY Email HAVING COUNT(*) > 1;

-- Project natural ID
SELECT ShortName, COUNT(*) as cnt FROM [Project] GROUP BY ShortName HAVING COUNT(*) > 1;

-- Provider natural ID (TO RUN)
SELECT Name, COUNT(*) as cnt FROM [Provider] GROUP BY Name HAVING COUNT(*) > 1;

-- Check unique constraints
SELECT 
    t.name as table_name,
    i.name as index_name,
    i.type_desc
FROM sys.indexes i
JOIN sys.tables t ON i.object_id = t.object_id
WHERE i.is_unique = 1 AND t.name IN ('User', 'Project', 'Provider')
ORDER BY t.name;
```

---

## 1.5 Current API Endpoint Usage Analysis

### User Endpoints

```
UserEndpoint
├── GET /User/info
│   ├── Current: Uses JWT token (no ID in route)
│   ├── After: No change (already uses Email from token)
│   └── Impact: ✅ LOW - No breaking change needed
├── GET /User/{userId}
│   ├── Current: Queries by UserId (UUID)
│   ├── After: Change to GET /User/{email}
│   └── Impact: ⚠️ BREAKING - Clients must use Email instead
└── PUT /User/{userId}
    ├── Current: Updates by UserId (UUID)
    ├── After: Change to PUT /User/{email}
    └── Impact: ⚠️ BREAKING - Request body should not include Id

UserInfoEndpoint
├── GET /user-info
│   ├── Current: Get authenticated user info
│   ├── After: No change (uses Email from token)
│   └── Impact: ✅ LOW - No breaking change

UserPermissionEndpoint (Implicit in UserEndpoint)
├── Current: Permissions accessed via User aggregate
├── After: Still accessed via User, but not directly queryable
└── Impact: ✅ SAFE - Child entity not exposed in API
```

### Project Endpoints

```
ProjectEndpoint
├── GET /Project/{projectShortName}
│   ├── Current: Uses ProjectShortName (already semantic!)
│   ├── After: No route change needed
│   └── Impact: ✅ NO CHANGE NEEDED
├── GET /Project/{projectId}
│   ├── Current: Some routes may use ID lookup
│   ├── After: Remove and consolidate to use ShortName
│   └── Impact: ⚠️ CHECK if this endpoint exists
├── POST /Project
│   ├── Current: Response includes Id in Location header
│   ├── After: Location header uses ShortName instead
│   └── Impact: ⚠️ BREAKING - Update clients parsing Location header
└── PUT /Project/{projectShortName}
    ├── Current: May accept UUID
    ├── After: Accept ShortName only
    └── Impact: ⚠️ BREAKING - Request body should not include Id

ProjectTagEndpoint
├── Current: Likely separate endpoints for tag CRUD
├── After: Tags accessed ONLY through Project aggregate
└── Impact: ⚠️ BREAKING - Cannot directly create/update/delete tags by ID
         Must use Project.AddTag(), Project.RemoveTag()
```

### Provider Endpoints

```
ProviderEndpoint
├── GET /Provider
│   ├── Current: List all providers (possibly queryable by Id)
│   ├── After: List queryable, but access by natural ID
│   └── Impact: ⚠️ REQUIRES VERIFICATION
└── Natural ID strategy: DEPENDS on Provider.Name uniqueness

ProjectResourceEndpoint
├── Current: Resources likely accessed by ResourceId
├── After: Resources accessed through Provider aggregate
└── Impact: ⚠️ BREAKING - Cannot directly create resources by ProjectResourceId
```

---

## 1.6 Breaking Change Impact Assessment

### Severity Level: 🔴 HIGH

This is a **major breaking change** affecting all API consumers.

#### Affected API Endpoints (Estimated 15+)

| Endpoint | Change Type | Severity | Migration Path |
|---|---|---|---|
| `GET /User/{userId}` | Route param | 🔴 HIGH | Switch to `GET /User/{email}` |
| `PUT /User/{userId}` | Route param | 🔴 HIGH | Switch to `PUT /User/{email}` |
| `GET /Project/{projectId}` | Route param | 🟠 MEDIUM | Switch to `GET /Project/{shortName}` (if exists) |
| `POST /Project` | Response body | 🟠 MEDIUM | Parse `shortName` instead of `id` from response |
| `PUT /Project/{projectId}` | Route param | 🔴 HIGH | Switch to `PUT /Project/{shortName}` |
| `GET /ProjectTag/{tagId}` | Route param | 🔴 HIGH | Remove endpoint, use `GET /Project/{shortName}` |
| `POST /ProjectTag` | Route param | 🔴 HIGH | Remove endpoint, use project aggregate methods |
| `DELETE /ProjectTag/{tagId}` | Route param | 🔴 HIGH | Remove endpoint, use project aggregate methods |
| `GET /Provider/{providerId}` | Route param | 🟠 MEDIUM | Switch to `GET /Provider/{name}` |
| `GET /ProjectResource/{resourceId}` | Route param | 🔴 HIGH | Access via provider aggregate only |
| `POST /ProjectResource` | Route param | 🔴 HIGH | Use provider aggregate methods |
| Response DTOs | Structure | 🟠 MEDIUM | Remove all `Id` fields from JSON responses |

#### Recommended Migration Strategy

**Option 1: Big Bang (Internal Systems)**
- Deploy new API only
- All clients update simultaneously
- Risk: High if many external clients

**Option 2: API Versioning (Recommended)**
- Keep v1 with UUIDs: `/v1/Project/{projectId}`
- Launch v2 with natural IDs: `/v2/Project/{shortName}`
- Support both for N months
- Clients migrate at their own pace

```
Timeline:
├── Month 1: Launch v2, mark v1 as deprecated
├── Month 1-3: Both versions operational
├── Month 3: Send final migration notices
├── Month 4: Sunset v1
└── Month 5: Remove v1 code
```

---

## 1.7 Relationship Mapping

### Inter-Aggregate Relationships (Shadow FKs)

```
User Aggregate ─────┐
                    │
                    ├─→ AssignedUserToProject (join table)
                    │
Project Aggregate ──┘

Project Aggregate ──┐
                    ├─→ AssignedProjectResourceToProject (join table)
                    │
Provider Aggregate ─┘

Provider Aggregate ─→ ProjectResource (owned)
```

### EF Configuration Strategy

For relationships between aggregate roots, use **shadow foreign keys**:

```csharp
// DO NOT expose FK properties in domain entities
// Instead, use shadow properties in EF configuration

// AssignedUserToProject joins User and Project
builder
    .Property<UserId>("UserId")  // Shadow property
    .IsRequired();
builder
    .Property<ProjectId>("ProjectId")  // Shadow property
    .IsRequired();

// Composite alternate key for natural lookup
builder.HasAlternateKey("UserId", "ProjectId");

// NO navigation properties between aggregates
```

---

## 1.8 Tasks for Phase 1 Completion

- [ ] **Verify Provider.Name uniqueness:**
  ```sql
  SELECT Name, COUNT(*) FROM Provider GROUP BY Name HAVING COUNT(*) > 1;
  ```

- [ ] **Verify Request (SelfOnboarding) natural identifier:**
  - Check if Email field exists
  - Check if there's already a unique identifier pattern
  - Recommend adding Email as natural ID if missing

- [ ] **Document AssignedLocation and AssignedLocationValue purpose:**
  - Are these aggregate roots or value objects?
  - Which aggregate do they belong to?
  - What's the natural identifier?

- [ ] **Document ActionLink purpose:**
  - What is this entity for?
  - Is it a reference table or value object?
  - Which aggregate does it belong to?

- [ ] **Verify UserProjectAccess usage:**
  - Is this used for direct lookups or only via aggregates?
  - Should it be removed (use AssignedUserToProject instead)?

- [ ] **Create API endpoint inventory:**
  - Map all endpoints using Id-based lookups
  - Identify which need to be refactored
  - Document deprecation strategy

- [ ] **Run breaking change analysis:**
  - Identify all external services consuming the API
  - Document their integration points
  - Plan migration communication

---

## 1.9 Deliverables Checklist

✅ **Completed:**
- [x] Identified 4 Aggregate Roots (User, Project, Provider, Request)
- [x] Documented child entities per aggregate
- [x] Listed shared/reference entities
- [x] Created entity-to-aggregate matrix
- [x] Documented natural identifiers (Email, ProjectShortName, Name)
- [x] Identified API breaking changes
- [x] Created relationship mapping
- [x] Provided API versioning strategy

⏳ **In Progress:**
- [ ] Verify natural identifier uniqueness in database
- [ ] Complete entity inventory (clarify shared entities)
- [ ] Detailed API endpoint analysis

📋 **Next Phase (Phase 2):**
- Refactor DatabaseContext into IReadUserOrgDbContext and IWriteUserOrgDbContext
- Update DI registrations
- Create new interfaces with proper entity exposure

---

## 1.10 Key Decisions Made

| Decision | Rationale |
|---|---|
| 4 Aggregate Roots (User, Project, Provider, Request) | Clear domain boundaries, separate lifecycles |
| Natural IDs: Email, ProjectShortName, Name | Semantic, stable, already in database |
| Shadow FKs for inter-aggregate relationships | DDD principle: no navigation between aggregates |
| Separate Read/Write DbContext interfaces | Enforce DDD boundaries, allow flexible reads |
| API versioning strategy | Minimize client disruption, phased migration |

---

## References

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[Entity Relationship Complete Map](./entity-relationship-complete-map.md)** - All 24 entities catalogued
- **[Phase 2: DbContext Refactoring](./ddd-migration-phase2-dbcontext.md)** - Next phase: Separate read/write contexts
- **[Full Plan Document](../../plan.md)** - Session planning and tracking
- **[Domain-Driven Design by Eric Evans](https://www.domainlanguage.com/ddd/)** - DDD principles reference
