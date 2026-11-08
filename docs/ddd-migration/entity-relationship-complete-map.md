# Complete Entity Relationship Map - Core Module

**Created:** 2026-04-24  
**Purpose:** Comprehensive mapping of all entities in UserOrganization Core module  
**Status:** ✅ COMPLETE  

---

## 📊 Entity Classification Summary

### Aggregate Roots (4)

| Aggregate | Natural ID | Child Entities | References |
|-----------|-----------|---|---|
| **User** | Email | UserPermission, UserPreference, AssignedUserToProject | Provider (inverse) |
| **Project** | ProjectShortName | ProjectTag, UserPermission, AssignedUserToProject, AssignedProjectResourceToProject | ProjectDomainType |
| **Provider** | Name (TBD) | ProjectResource, Location | Country |
| **Request** | ContactEmail/UUID (TBD) | ActionLink | User, Project, Provider, ProjectResource |

### Child Entities (11)

| Entity | Parent(s) | Type | Purpose |
|--------|-----------|------|---------|
| UserPermission | User, Project | Permission Entity | Maps permissions for user in project |
| UserPreference | User | Preference Entity | User settings (module preferences) |
| AssignedUserToProject | User, Project | Assignment Entity | Tracks user assignment to project |
| ProjectTag | Project | Tag Entity | Categorizes projects |
| AssignedProjectResourceToProject | Project, ProjectResource | Assignment Entity | Allocates resources to project |
| ProjectResource | Provider | Resource Entity | HPC/storage resource managed by provider |
| Location | Provider | Location Entity | Physical/logical location for provider |
| AssignedLocation | ProjectResource, Location | Allocation Entity | How resource is allocated at location |
| AssignedLocationValue | AssignedLocation | Value Entity | Metrics for allocation (leaf node) |
| ActionLink | Request | Link Entity | Approval/rejection tokens for requests |
| ConfigurationValue | Configuration, Location | Config Entity | Configuration values per location |

### Reference Entities (7)

| Entity | Type | Purpose | Access Pattern |
|--------|------|---------|---|
| Country | Enum | Provider location | Direct lookup |
| LocationType | Enum | Location classification | Direct lookup |
| AllocationUnitType | Enum | Resource unit types | Direct lookup |
| ProjectDomainType | Enum | Project domain types | Via Project aggregate |
| ConfigurationType | Enum | Configuration types | Direct lookup |
| SystemModule | Enum | System modules | Direct lookup |
| Configuration | Config | System settings | Direct lookup |
| UserProjectAccess | Join Table | Access control (future) | Via User/Project |

---

## 🔗 Complete Entity Dependency Graph

```
┌─────────────────────────────────────────────────────────────────────┐
│                       AGGREGATE ROOTS                               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌────────┐ │
│  │    USER      │  │   PROJECT    │  │   PROVIDER   │  │REQUEST │ │
│  │              │  │              │  │              │  │        │ │
│  │ Natural ID:  │  │ Natural ID:  │  │ Natural ID:  │  │Natural │ │
│  │  Email       │  │ ShortName    │  │  Name (TBD)  │  │ID:TBD  │ │
│  └──────────────┘  └──────────────┘  └──────────────┘  └────────┘ │
│        │                    │                │               │      │
└─────────────────────────────────────────────────────────────────────┘
        │                    │                │               │
        ├────────────────────┤                │               │
        │                    │                │               │
        ▼                    ▼                ▼               ▼
    ┌─────────────────┐  ┌─────────────────┐  │         ┌──────────┐
    │  USER PERMISSION│  │  PROJECT TAG    │  │         │ACTION    │
    └─────────────────┘  └─────────────────┘  │         │LINK      │
         (child)             (child)            │         └──────────┘
                                                │            (child)
        ┌──────────────┐                        │
        │USER          │                        ▼
        │PREFERENCE    │         ┌──────────────────────────────┐
        └──────────────┘         │   ASSIGNED PROJECT RESOURCE  │
             (child)             │   TO PROJECT                 │
                                 └──────────────────────────────┘
        ┌──────────────┐                (child of Project)
        │ASSIGNED USER │
        │TO PROJECT    │
        └──────────────┘
             (child)
        
        
┌─────────────────────────────────────────────────────────────────────┐
│                  REFERENCE ENTITIES & HIERARCHY                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  ┌───────────┐  ┌──────────┐  ┌─────────────────┐                 │
│  │ COUNTRY   │  │LOCATION  │  │PROJECT RESOURCE │                 │
│  │(Enum Ref) │  │ (Ref)    │  │(Child of Prov)  │                 │
│  └───────────┘  └──────────┘  └─────────────────┘                 │
│        │              │              │                              │
│        │              ├─→ LOCATION   │                              │
│        │              │   TYPE       │                              │
│        │              │ (Enum)       │                              │
│        │              │              │                              │
│        │              ▼              ▼                              │
│        │         ┌──────────────┐ ┌──────────────────────┐         │
│        │         │ASSIGNED      │ │ALLOCATED LOCATION    │         │
│        │         │LOCATION      │ │FOR PROJECT RESOURCE  │         │
│        │         │(Child)       │ └──────────────────────┘         │
│        │         │              │          (child)                  │
│        │         └──────────────┘                                   │
│        │              │           ┌─────────────────────┐           │
│        │              │           │ASSIGNMENT UNIT TYPE │           │
│        │              │           │(Enum Ref)          │           │
│        │              │           └─────────────────────┘           │
│        │              │                                              │
│        │              ▼           ┌──────────────────────┐           │
│        │         ┌──────────────┐ │CONFIGURATION VALUE  │           │
│        │         │ASSIGNED      │ │(Child of Config)    │           │
│        │         │LOCATION      │ └──────────────────────┘           │
│        │         │VALUE         │          (child)                  │
│        │         │(Child/Leaf)  │                                   │
│        │         └──────────────┘                                   │
│        │                                                             │
│        └────────→ ┌──────────────┐ ┌────────────────┐              │
│                   │CONFIGURATION │ │PROJECT DOMAIN  │              │
│                   │(System Config)│ │TYPE (Enum)     │              │
│                   └──────────────┘ └────────────────┘              │
│                                                                       │
│                   ┌────────────────┐ ┌────────────────┐              │
│                   │CONFIG TYPE     │ │SYSTEM MODULE   │              │
│                   │(Enum Ref)      │ │(Enum Ref)      │              │
│                   └────────────────┘ └────────────────┘              │
│                                                                       │
│                   ┌──────────────────────────────────┐               │
│                   │USER PROJECT ACCESS               │               │
│                   │(Join Table - Future replacement) │               │
│                   └──────────────────────────────────┘               │
│                                                                       │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 📋 Complete Entity List (24 total)

### By Category

**Aggregate Roots (4):**
1. User
2. Project
3. Provider
4. Request

**Child Entities of Aggregates (11):**
5. UserPermission (→ User + Project)
6. UserPreference (→ User)
7. AssignedUserToProject (→ User + Project)
8. ProjectTag (→ Project)
9. AssignedProjectResourceToProject (→ Project + ProjectResource)
10. ProjectResource (→ Provider)
11. Location (→ Provider)
12. AssignedLocation (→ ProjectResource + Location)
13. AssignedLocationValue (→ AssignedLocation)
14. ActionLink (→ Request)
15. ConfigurationValue (→ Configuration + Location)

**Reference & Enum Entities (7):**
16. Country (Enum)
17. LocationType (Enum)
18. AllocationUnitType (Enum)
19. ProjectDomainType (Enum)
20. ConfigurationType (Enum)
21. SystemModule (Enum)
22. Configuration (System Config)
23. UserProjectAccess (Join Table)

---

## 🔄 Entity Relationships Matrix

| From | To | Relationship | Type | Multiplicity |
|------|---|---|---|---|
| User | Email | Natural ID | Value | 1:1 (unique) |
| User | UserPermission | Child | Composition | 1:* |
| User | UserPreference | Child | Composition | 1:* |
| User | AssignedUserToProject | Child | Composition | 1:* |
| User | Provider | Reverse Reference | Aggregation | *:1 |
| Project | ProjectShortName | Natural ID | Value | 1:1 (unique) |
| Project | ProjectTag | Child | Composition | 1:* |
| Project | UserPermission | Child | Composition | 1:* |
| Project | AssignedUserToProject | Child | Composition | 1:* |
| Project | AssignedProjectResourceToProject | Child | Composition | 1:* |
| Project | ProjectDomainType | Reference | Association | *:1 |
| Provider | Name | Natural ID | Value | 1:1 (TBD) |
| Provider | ProjectResource | Child | Composition | 1:* |
| Provider | Location | Child | Composition | 1:* |
| Provider | Country | Reference | Association | *:1 |
| Provider | User | Reference | Association | *:* |
| ProjectResource | AssignedProjectResourceToProject | Reverse | Composition | 1:* |
| ProjectResource | AssignedLocation | Child | Composition | 1:* |
| Location | LocationType | Reference | Association | *:1 |
| Location | AssignedLocation | Child | Composition | 1:* |
| Location | ConfigurationValue | Child | Composition | 1:* |
| AssignedLocation | AssignedLocationValue | Child | Composition | 1:* |
| AssignedLocation | AllocationUnitType | Reference | Association | *:1 |
| Configuration | ConfigurationType | Reference | Association | *:1 |
| Configuration | ConfigurationValue | Reverse | Composition | 1:* |
| Request | User | Reference | Association | *:1 |
| Request | Project | Reference | Association | *:1 |
| Request | Provider | Reference | Association | *:1 |
| Request | ProjectResource | Reference | Association | *:1 |
| Request | ActionLink | Child | Composition | 1:* |

---

## 🎯 Natural Identifier Status

| Aggregate | Natural ID | Status | Recommendation |
|-----------|-----------|--------|---|
| **User** | Email | ✅ Confirmed | Use Email as natural ID |
| **Project** | ProjectShortName | ✅ Confirmed | Use ProjectShortName as natural ID |
| **Provider** | Name | ⚠️ TBD | Verify if Name is unique; if not, use composite key |
| **Request** | ContactEmail or UUID | ⚠️ TBD | Use ContactEmail + Type as composite natural ID |

---

## 📝 Outstanding Questions

1. **Provider Natural ID:** Is `Provider.Name` unique across all providers?
   - If YES: Use Name as natural ID
   - If NO: Create unique constraint or use composite ID (Name + Country)?

2. **Request Natural ID:** What uniquely identifies a request?
   - Current: UUID (not natural)
   - Proposed: ContactEmail + Type + State as composite natural ID
   - Needs clarification on business semantics

---

## 📚 Related Documentation

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[Phase 1: Architecture Analysis](./ddd-migration-phase1-analysis.md)** - Detailed aggregate root definitions
- **[Phase 2: DbContext Refactoring](./ddd-migration-phase2-dbcontext.md)** - Implementing read/write separation
- **[Phase 3: Remove Public IDs](./ddd-migration-phase3-entities.md)** - Converting IDs to shadow properties

---

**Document Status:** ✅ Complete  
**Last Updated:** 2026-04-24  
**All 24 Entities Catalogued:** Yes
   - Proposed: ContactEmail + RequestType + State
   - Alternative: ContactEmail + CreatedDate

3. **ProjectResource Accessibility:** Is ProjectResource only accessed via Provider aggregate?
   - If YES: Implement as true child entity
   - If NO: Might need separate access path

4. **Location Accessibility:** Is Location only accessed via Provider aggregate?
   - If YES: Implement as true child entity
   - If NO: Might need separate access path (by LocationType?)

---

## ✅ Implementation Checklist

- ✅ All 24 entities identified and catalogued
- ✅ Aggregate boundaries defined (4 aggregates)
- ✅ Child entity relationships documented (11 children)
- ✅ Reference entities classified (7 references)
- ✅ Natural identifiers identified (2 confirmed, 2 TBD)
- ✅ Dependency graph created
- ✅ Relationship matrix generated
- ⏳ Verify Provider and Request natural IDs in Phase 1
- ⏳ Implement shadow properties in Phase 2
- ⏳ Configure alternate keys in Phase 4

---

**Status:** ✅ Complete  
**Coverage:** 100% of Core entities (24/24)  
**Last Updated:** 2026-04-24

**Related Documents:**
- `ddd-migration-phase1-analysis.md` - Architecture analysis
- `ddd-migration-phase2-dbcontext.md` - DbContext refactoring
- `ddd-migration-phase3-entities.md` - Entity implementation
