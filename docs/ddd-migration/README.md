# DDD Migration Documentation Suite

**Complete implementation guide for migrating UserOrganization Core to Domain-Driven Design**

⏱️ **Last Updated:** 2026-07-13  
📊 **Status:** Endpoint migration **complete** — every legacy Core `/api/*` route is ported to `/v2/*`, re-homed to another module (DDI / Heappe), or consciously dropped. Both stacks still run in parallel; remaining work before deleting `src/Core` is write-parity testing + legacy teardown, not endpoint coverage. Authoritative ledger: [v1-retirement-parity.md](./v1-retirement-parity.md)  
🎯 **Goal:** Transform from Anemic Domain Model → Rich DDD Model with hidden IDs and separated DbContext

> **Where the action is now:** [Core Modules — Target Architecture & Refactor Plan](./core-modules-target-architecture-and-plan.md)
> (P0–P3 complete), [Provider & Location write endpoints](./provider-location-write-endpoints-plan.md) (✅ implemented),
> [Resource-assignment write endpoints](./resource-assignment-write-endpoints-plan.md) (✅ implemented — ADR-0016; resource↔project assignment F5, ADR-0031).
> The phase docs below (Phases 1–6) are the original analysis groundwork and predate the modular-core plan.

---

## 📚 Reading Order & Navigation

This documentation is organized in **reading order** from foundation to implementation. Start with the **Overview** section, then follow the **Sequential Reading Path**.

### 🎓 Overview & Foundation

**Start here** if you're new to this migration or need a quick understanding:

1. **[Entity Relationship Complete Map](./entity-relationship-complete-map.md)** ⭐ START HERE
   - **Purpose:** Complete entity landscape overview
   - **Contents:**
     - All 24 Core entities catalogued
     - 4 Aggregate Roots with full child hierarchies
     - 11 Child entities with ownership semantics
     - 7 Reference & Enum entities
     - Complete dependency graph
     - Entity relationship matrix (30+ relationships)
   - **Read Time:** 15 minutes
   - **Why First:** Understand the complete entity structure before diving into phases

2. **[Legacy vs. Refactored Entity Access Guide](./legacy-vs-refactored-entity-access-guide.md)** ⭐ NEW - PRACTICAL GUIDE
   - **Purpose:** Compare how entities are accessed before and after DDD refactoring
   - **Contents:**
     - Aggregate root hierarchy with visual structure
     - Entity access patterns (legacy UUID vs. refactored natural ID)
     - API endpoint migration examples (V1 → V2)
     - Child entity access patterns and restrictions
     - Step-by-step migration guide for client code
     - Complete aggregate structure reference
   - **Read Time:** 20-25 minutes
   - **Best For:** Understanding practical differences, migrating client code, API design decisions

3. **[Cross-Aggregate Boundaries in DDD](./cross-aggregate-boundaries.md)** ⭐ NEW - BOUNDARIES & ISOLATION
   - **Purpose:** Enforce proper DDD boundaries when aggregates reference each other
   - **Contents:**
     - Why direct aggregate references violate DDD
     - Pattern: Using shadow properties (IDs) instead of object references
     - Join entities between aggregates
     - How to query and modify across aggregate boundaries
     - Anti-patterns and how to avoid them
     - Cross-aggregate reference inventory
     - Boundary enforcement checklist
   - **Read Time:** 15-20 minutes
   - **Best For:** Understanding isolation patterns, querying practices, preventing tight coupling

2. **[Phase 1: Architecture & Pattern Analysis](./ddd-migration-phase1-analysis.md)**
   - **Purpose:** Understand current architecture and planned aggregate boundaries
   - **Contents:**
     - 4 Aggregate Roots detailed (User, Project, Provider, Request)
     - Natural identifier inventory with verification status
     - Breaking change impact assessment
     - API endpoint usage analysis
     - Entity-to-aggregate mapping matrix
   - **Read Time:** 20-30 minutes
   - **Prerequisites:** None (foundational)
   - **Deliverables:** Architecture blueprint, Entity mapping matrix

---

### 📋 Sequential Implementation Path

Follow these documents **in order** for implementation:

#### **Phase 1: Architecture Analysis** ✅ COMPLETE
**[Phase 1 Documentation](./ddd-migration-phase1-analysis.md)**
- 🎯 **Goal:** Understand all aggregate roots and entity boundaries
- 📊 **Deliverable:** Aggregate boundary definitions, Entity mapping matrix
- ✓ **Status:** Complete and verified
- **Key Concepts:**
  - User Aggregate (Email natural ID)
  - Project Aggregate (ProjectShortName natural ID)
  - Provider Aggregate (Name natural ID - TBD verification)
  - Request Aggregate (ContactEmail/UUID natural ID - TBD definition)

---

#### **Phase 2: DbContext Refactoring** 📋 PENDING
**[Phase 2 Documentation](./ddd-migration-phase2-dbcontext.md)**
- 🎯 **Goal:** Separate single context into Read/Write interfaces
- 📊 **Deliverable:**
  - `IReadUserOrgDbContext` interface (all entities queryable)
  - `IWriteUserOrgDbContext` interface (aggregate roots only)
  - Updated `DatabaseContext` implementation
  - DI registration configuration
- **Prerequisites:** Phase 1 complete
- **Key Decisions:**
  - Read context provides flexibility for queries
  - Write context enforces aggregate root boundaries
  - Both contexts share same underlying DbContext
- **Estimated Time:** 1 day

---

#### **Phase 3: Remove Public ID Properties** 📋 PENDING
**[Phase 3 Documentation](./ddd-migration-phase3-entities.md)**
- 🎯 **Goal:** Convert public IDs to shadow properties, add domain methods
- 📊 **Deliverable:**
  - Updated User entity (no public Id)
  - Updated Project entity (no public Id)
  - Updated Provider entity (no public Id)
  - Updated Request entity (no public Id)
  - 11 child entities updated
  - 7 reference entities updated
- **Prerequisites:** Phase 2 complete
- **Key Techniques:**
  - Shadow properties via EF configuration
  - Domain methods for business logic
  - Natural identifiers in queries
- **Breaking Changes:** ⚠️ HIGH - All domain entities change
- **Estimated Time:** 1-2 days

---

#### **Phase 4-5: EF Configuration & Repositories** 📋 PENDING
*Covered in Phase 2 & 3 documentation*
- Configure shadow properties for all IDs
- Set up alternate keys for natural identifiers
- Update repository pattern for natural ID queries
- Create query-by-natural-ID patterns

---

#### **Phase 6: API Versioning & Endpoint Refactoring** 🔥 ACTIVE
**Versioning Documentation (Read in order):**

**6a. [API Versioning via Gateway - Path-Based Strategy](./api-versioning-gateway-routing.md)** ⭐ PRIMARY
- **Purpose:** Understand Gateway-based versioning approach
- **Contents:**
  - Gateway routing architecture with YARP
  - V1 vs V2 endpoint structure
  - Path-based routing strategy
  - Deployment configuration
  - Testing patterns
- **Read Time:** 20 minutes
- **Key Concept:** Gateway routes `/projects` → `/api/projects` (v1) and `/v2/Projects` → `/api/v2/Projects` (v2)

**6b. [Model Versioning Strategy - Incremental Evolution](./model-versioning-evolution.md)**
- **Purpose:** Handle model evolution without endpoint explosion
- **Contents:**
  - When to evolve models vs. create new versions
  - Backward compatibility patterns
  - @Obsolete DTO marking
  - Decision trees
  - Communication templates
- **Read Time:** 15 minutes
- **Key Concept:** Add optional fields to existing models instead of creating v3, v4, etc.

**6c. [Phase 6: API Endpoint Refactoring with Gateway Versioning](./ddd-migration-phase6-api-versioning.md)** ⭐ IMPLEMENTATION GUIDE
- **Purpose:** 8-step practical implementation guide for Phase 6
- **Contents:**
  - Create V1 controllers (/api/projects)
  - Create V2 controllers (/api/v2/Projects)
  - Version-specific DTOs
  - Testing strategy
  - Deployment validation
  - Gateway configuration checklist
- **Prerequisites:** Phases 1-5 complete, Gateway versioning strategy understood
- **Read Time:** 30-40 minutes
- **Deliverables:** Both V1 and V2 endpoints functional, tested, documented
- **Estimated Time:** 1-2 days

---

#### **Phase 7-10: Application Services, Migrations, Tests, Validation** 📋 PENDING
- Update application services to use new contexts
- Create EF Core migrations
- Write comprehensive test suite
- End-to-end testing

---

## 🎯 Quick Reference by Use Case

### "I need to understand the entity landscape"
→ [Entity Relationship Complete Map](./entity-relationship-complete-map.md)

### "I need to understand how entities are accessed in legacy vs. refactored approach"
→ [Legacy vs. Refactored Entity Access Guide](./legacy-vs-refactored-entity-access-guide.md)

### "I need to understand how aggregates should reference each other"
→ [Cross-Aggregate Boundaries in DDD](./cross-aggregate-boundaries.md)

### "I need to learn the pattern for join entities between aggregates"
→ [Cross-Aggregate Boundaries in DDD](./cross-aggregate-boundaries.md) (Implementation Pattern section)

### "I'm getting tight coupling between aggregates - what's wrong?"
→ [Cross-Aggregate Boundaries in DDD](./cross-aggregate-boundaries.md) (Anti-Patterns section)

### "I need to migrate API client code from UUID-based to natural ID-based access"
→ [Legacy vs. Refactored Entity Access Guide](./legacy-vs-refactored-entity-access-guide.md) (Migration Guide section)

### "I need to understand aggregate root hierarchies and child entities"
→ [Legacy vs. Refactored Entity Access Guide](./legacy-vs-refactored-entity-access-guide.md) (Aggregate Roots Overview + Complete Aggregate Structure Reference)

### "I'm implementing Phase 1 (Architecture Analysis)"
→ [Phase 1: Architecture & Pattern Analysis](./ddd-migration-phase1-analysis.md)

### "I'm implementing Phase 2 (DbContext)"
→ [Phase 2: DbContext Refactoring](./ddd-migration-phase2-dbcontext.md)

### "I'm implementing Phase 3 (Remove Public IDs)"
→ [Phase 3: Remove Public ID Properties](./ddd-migration-phase3-entities.md)

### "I'm implementing Phase 6 (API Versioning)"
Read in order:
1. [API Versioning via Gateway](./api-versioning-gateway-routing.md)
2. [Model Versioning Strategy](./model-versioning-evolution.md)
3. [Phase 6 Implementation Guide](./ddd-migration-phase6-api-versioning.md)

### "I need versioning strategy decision trees"
→ [Model Versioning Strategy](./model-versioning-evolution.md) (has decision trees)

### "I need the target architecture & refactor plan for the Core/Core.Domain/Core.DataAccess projects"
→ [Core Modules — Target Architecture & Refactor Plan](./core-modules-target-architecture-and-plan.md) (dependency direction, per-project findings, prioritized steps)

### "I need to know where a legacy endpoint went in v2 and what breaks"
→ [Legacy → Refactored Endpoint Migration Map](./legacy-to-refactored-endpoint-migration.md) (per-endpoint legacy↔v2 mapping, migration status, breaking-change summaries)

### "I'm adding a new v2 endpoint — what's the reference pattern?"
→ [Golden Slice](./golden-slice.md) (reference read + write vertical slice and the add-an-endpoint checklist)

### "I need the provider/location write design (auth model, name canonicalization)"
→ [Provider & Location write endpoints plan](./provider-location-write-endpoints-plan.md) (✅ implemented) + ADR-0011/0012

### "I'm implementing the resource-assignment writes"
→ [Resource-assignment write endpoints plan](./resource-assignment-write-endpoints-plan.md) (S1–S6 steps) + [ADR-0016](../architecture/adr/0016-resource-assignment-writes-through-provider-aggregate.md)

---

## 🔗 Cross-References & Key Concepts

### Aggregate Roots (Defined in Phase 1, Used in Phases 2-6)

| Aggregate | Natural ID | Child Entities | Verification Status |
|-----------|-----------|---|---|
| **User** | Email | UserPermission, UserPreference, AssignedUserToProject | ✅ Verified |
| **Project** | ProjectShortName | ProjectTag, UserPermission, AssignedUserToProject, AssignedProjectResourceToProject | ✅ Verified |
| **Provider** | Name | ProjectResource, Location, Country (ref) | ✅ Resolved — natural key end-to-end (route, repo key, claim); no DB unique index (migrations-overlay rule), uniqueness enforced in app code incl. soft-deleted ([ADR-0011](../architecture/adr/0011-provider-write-authorization.md)) |
| **Request** | Surrogate Guid | ActionLink, User (ref), Project (ref), Provider (ref) | ✅ Resolved — no natural composite adopted; v2 addresses requests by `requestId:guid` ([ADR-0009](../architecture/adr/0009-request-write-path-self-onboarding-only.md)) |

### Shadow Properties (Explained in Phase 3, Configured in Phase 4)

All 24 entities will have `Id` properties converted from **public** to **shadow properties**:
- Domain layer: `public class Project { /* No public Id */ }`
- EF Configuration: `builder.Property<ProjectId>("Id")`
- Access: Only through EF, not in domain code

### API Versioning Strategy (Documented in Phase 6 docs)

**Gateway-Based Path Versioning:**
```
V1 (UUID): GET /projects/{id}           → /api/projects/{id}
V2 (Natural ID): GET /v2/projects/{id}  → /api/v2/Projects/{id}
```

**Model Evolution (Not Endpoint Versioning):**
- Adding optional field → Evolve model (ProjectV2Dto → ProjectV2Dto with optional field)
- Removing required field → Create new endpoint version (/api/v3/)

---

## 📊 Dependency Graph

```
Entity Relationship Map (foundation knowledge)
        ↓
Phase 1: Architecture Analysis (understand boundaries)
        ↓
Phase 2: DbContext Refactoring (separate read/write)
        ├─ Phase 3: Remove Public IDs (shadow properties)
        ├─ Phase 4-5: EF Configuration (alternate keys, indexes)
        ├─ Repository Updates (natural ID queries)
        ↓
Phase 6: API Versioning
        ├─ API Versioning via Gateway (architecture)
        ├─ Model Versioning Strategy (evolution patterns)
        ├─ Phase 6 Implementation (8 steps)
        ↓
Phase 7: Application Services Update
        ↓
Phase 8: EF Core Migrations
        ↓
Phase 9: Test Suite
        ↓
Phase 10: Validation & Go-Live
```

---

## ✅ Verification Checklist

Use this checklist to track progress through the documentation:

- [ ] Read Entity Relationship Complete Map (foundational)
- [ ] Read Phase 1: Architecture Analysis
- [ ] Understand all 4 aggregate roots
- [ ] Identify TBD items (Provider.Name uniqueness, Request natural ID)
- [ ] Review Phase 2: DbContext Refactoring approach
- [ ] Review Phase 3: Shadow properties strategy
- [ ] Review Phase 6 versioning strategy (Gateway approach)
- [ ] Understand model evolution vs. endpoint versioning
- [ ] Plan Phase 6 implementation steps
- [ ] Schedule implementation phases

---

## 🔄 Consistency Notes

### Verified Consistency Across Documents

✅ **Entity Names:** Consistent across all 7 documents
- User, Project, Provider, Request aggregate roots
- All 11 child entities named consistently
- All 7 reference entities named consistently

✅ **Natural Identifiers:** Consistent definitions
- User: Email ✅ Verified unique
- Project: ProjectShortName ✅ Verified unique
- Provider: Name ✅ Natural key end-to-end (ADR-0011); app-enforced uniqueness (no DB index)
- Request: ✅ GUID-free generated slug `req-{...}` (ADR-0024) — no ContactEmail/UUID natural key

✅ **Gateway Versioning Strategy:** Aligned across documents
- api-versioning-gateway-routing.md (primary strategy doc)
- ddd-migration-phase6-api-versioning.md (references gateway approach)
- model-versioning-evolution.md (complements with model strategy)

✅ **API Versioning Principles:**
- All docs use same versioning model
- Clear distinction between endpoint versioning (v1, v2) and model evolution
- Consistent Gateway routing pattern

### Outstanding Verification Items (both resolved)

✅ **Provider.Name Uniqueness** — resolved by [ADR-0011](../architecture/adr/0011-provider-write-authorization.md):
`ProviderName` is the natural key end-to-end (route, repository key, `providerName` claim). The column has
**no DB unique index** and the migrations-overlay rule forbids adding one, so uniqueness is enforced in
application code at create time (pre-check including soft-deleted rows → `409`). Names are canonicalized
([ADR-0012](../architecture/adr/0012-provider-location-name-canonicalization.md)).

✅ **Request Natural Identifier** — resolved by [ADR-0024](../architecture/adr/0024-v2-contracts-zero-surrogate-guids.md)
(superseding the earlier surrogate approach): no natural composite was adopted and the surrogate `requestId`
GUID is **not** exposed; v2 addresses requests by a generated GUID-free slug `req-{e3}-{projectShortName}-{r6}`
(`requests/{slug}`), and the anonymous public read resolves by single-use action-link token
(`v2/Public/Requests/ByLink/{actionLink}`).

---

## 📞 Questions During Reading?

If you encounter unclear concepts while reading:

1. **Entity relationships unclear?** → Check Entity Relationship Complete Map (dependency graph section)
2. **Why this aggregate structure?** → See Phase 1 (Why X is an Aggregate Root)
3. **How to configure shadow properties?** → See Phase 3 (EF configuration examples)
4. **API versioning confusion?** → Read both gateway and model versioning docs
5. **Implementation checklist?** → See individual phase docs (each has step-by-step checklist)

---

## 📁 File Organization

```
docs/ddd-migration/
├── README.md (← YOU ARE HERE)
├── entity-relationship-complete-map.md
├── legacy-vs-refactored-entity-access-guide.md
├── cross-aggregate-boundaries.md
├── ddd-migration-phase1-analysis.md
├── ddd-migration-phase2-dbcontext.md
├── ddd-migration-phase3-entities.md
├── ddd-migration-phase6-api-versioning.md
├── api-versioning-gateway-routing.md
├── model-versioning-evolution.md
├── core-modules-target-architecture-and-plan.md (target arch + refactor plan, P0–P3 ✅)
├── core-modules-axis-a-p1-implementation.md (P1 implementation notes)
├── golden-slice.md (reference vertical slice + add-an-endpoint checklist)
├── prd-core-claims-first.md (claims-first PRD)
├── plan-project-tag-split-and-readonly.md (tag split + read-only plan, ✅)
├── provider-location-write-endpoints-plan.md (provider/location writes, ✅)
├── resource-assignment-write-endpoints-plan.md (resource-assignment writes, planned — ADR-0016)
├── v2-endpoint-test-plan.md (v2 endpoint test plan)
└── legacy-to-refactored-endpoint-migration.md (legacy↔v2 endpoint map — live status)
```

---

## 🚀 Getting Started

**Recommend this flow:**

1. **First time?** Read Entity Relationship Map + Phase 1 (30-45 min)
2. **Implementing Phase 2?** Read Phase 2 doc + understand DbContext strategy
3. **Implementing Phase 6?** Read all 3 versioning docs (gateway + evolution + phase 6)
4. **Quick reference?** Use the "Quick Reference by Use Case" section above

---

## 📈 Migration Timeline

> ⚠️ **Superseded.** This 10-phase table is the *original* plan and its `📋 Pending` / `🔥 Ready` statuses
> are historical — the work was reorganized into the modular-core plan (P0–P3) and the F0–F7 slices, all of
> which have shipped. For live status use [v1-retirement-parity.md](./v1-retirement-parity.md) and
> [progress.md](./progress.md). Kept for provenance.

| Phase | Focus | Status | Est. Time |
|-------|-------|--------|-----------|
| 1 | Architecture Analysis | ✅ Complete | 30-45 min read |
| 2 | DbContext Refactoring | 📋 Pending | 1 day impl |
| 3 | Remove Public IDs | 📋 Pending | 1-2 days impl |
| 4-5 | EF Config & Repositories | 📋 Pending | 1 day impl |
| 6 | API Versioning | 🔥 Ready | 1-2 days impl |
| 7 | Application Services | 📋 Pending | 1 day impl |
| 8 | EF Migrations | 📋 Pending | 1 day impl |
| 9 | Test Suite | 📋 Pending | 2-3 days impl |
| 10 | Validation & Go-Live | 📋 Pending | 1 day impl |

---

**Happy reading! 📚 Start with [Entity Relationship Complete Map](./entity-relationship-complete-map.md)**
