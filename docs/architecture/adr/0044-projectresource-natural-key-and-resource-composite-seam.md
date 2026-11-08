# ADR 0044 — ProjectResource natural identifier is `(ProviderName, ResourceName)`; Resource modelled as a Composite (Project-as-Resource seam)

- Date: 2026-06-29
- Status: Accepted (accepted 2026-09-08 by the repository owner; the code has implemented it in full since before that date)
- Builds on: **ADR-0043** (ProjectResource stays an aggregate root; Provider is the create-and-allocate factory).
- Supersedes (modular `Modules/Core*` only): **ADR-0016**'s "resource referenced cross-aggregate by the typed
  surrogate `ProjectResourceId`" — `AssignedLocation` now references the resource by an EF **navigation +
  shadow FK**, and `AssignResource` takes the resolved **entity** (Level 2 below). Also amends **ADR-0030 §4**
  (invariant #5 moves from the use-case into the aggregate). Legacy `src/Core` is untouched.
- Relates to: ADR-0026 (per-provider identity), ADR-0029 (natural keys at boundaries; surrogate stays internal),
  F5 (resource→project link — already navigation/natural-key shaped).

## Context

`ProjectResource` is confirmed an aggregate root (ADR-0043 D1). Two things still need pinning:

1. **A determined natural identifier** so other aggregates communicate with it by domain identity, not by a
   surrogate `Guid` (ADR-0029). Today two cross-aggregate links exist — `AssignedLocation` (resource→location,
   ADR-0016) and `AssignedProjectResourceToProject` (resource→project, F5) — and both store the **surrogate**
   `ProjectResourceId`; the `Project.Resources` read projection even exposes only `ResourceName` (no provider),
   which is under-specified.
2. **A near-future feature (within months):** assigning a *whole `Project`* as a "resource" to another project,
   where that resource must behave as **the set of `ProjectResource`s contained in the source project**. The
   aggregate model must accommodate this now as a clean additive seam, not a later remodel.

## Decision

1. **Natural identifier = `(ProviderName, ResourceName)`**, formalised as a value object
   **`ProjectResourceKey(ProviderName Provider, ResourceName Name)`**. `ResourceName` alone is **not** unique —
   ADR-0026 allows the same name under different providers (the cross-provider single row was the bug they
   split). The surrogate `ProjectResourceId` stays a **storage** detail (join-table FKs, EF) and never crosses a
   contract (ADR-0029 §3). The `Project.Resources` read projection gains `ProviderName` alongside `ResourceName`.

2. **Resource is a Composite.** What gets assigned to a `Project` is a *Resource* that is either a **leaf** (one
   provider-owned `ProjectResource`) or a **composite** (a whole `Project`, which expands to that project's leaf
   resources). The cross-aggregate identifier is a discriminated value object:

   ```
   ResourceRef =
      | Single(ProjectResourceKey)      // (ProviderName, ResourceName) — a leaf
      | ProjectSet(ProjectShortName)    // a whole project — composite
   ```

3. **Expansion is lazy, recursive, with cycle detection — and never stored.** A port
   **`IResourceExpander.ExpandAsync(ResourceRef) → IReadOnlyList<ProjectResourceKey>`** resolves a ref to its
   flat leaf set at **read / sync time** (`Single` → `[key]`; `ProjectSet(p)` → p's active leaf resources,
   resolved **transitively**). The aggregate stores the *reference*, not a snapshot, so the set always reflects
   the source project's current resources. The expander **must detect cycles** (P1↔P2) and bound recursion.

4. **Link storage (future, additive).** When the composite path is built,
   `AssignedProjectResourceToProject` generalises to `AssignedResourceToProject` carrying a `ResourceRef`:
   nullable `ProjectResourceId` (leaf, today's column) **+** nullable `SourceProjectId` (composite) **+** a
   `CHECK` that exactly one is set. `Project.AssignResource(ResourceRef)` takes the ref; **self-assignment is
   forbidden**. No rewrite of the leaf path.

5. **Scope of the current feature.** The create-and-allocate work (ADR-0043) implements **only the leaf /
   provider-owned path** and introduces the `ProjectResourceKey` value object. The `ProjectSet` / composite path
   (discriminated link column, `IResourceExpander`, recursive resolve, read-side expansion) is a **designed,
   deferred seam** — built by the future feature, not now (YAGNI: no composite use-case exists yet).

## Level 2 — the surrogate `ProjectResourceId` is fully encapsulated (storage-only)

The natural key (§1) is the *communication* identity; the surrogate `ProjectResourceId` (DB `Guid`) must be a
**pure persistence detail** — not a public Domain member, not returned by a query contract, not an
aggregate-method parameter, not a repository key. Scope: **`Modules/Core*` only**. Concretely:

L2.1 **`AssignedLocation` references the resource by navigation, not by id-value.** Replace the public
`AssignedLocation.ProjectResourceId` property with a `private ProjectResource? _resource` navigation mapped to
the **shadow FK** `"ProjectResourceId"` (`HasOne<ProjectResource>("_resource").WithMany().HasForeignKey("ProjectResourceId")`),
mirroring `ProjectResource._provider` (ADR-0031). The column, FK constraint and indexes are **unchanged — no
schema migration**. The read/sync surface reads the resource's natural key via a projection
(`ResourceName`/`ProjectResourceKey`), never the `Guid`.

L2.2 **Aggregate methods take the resolved entity.** `Location.AssignResource(ProjectResource resource, …)` and
`Provider.AssignResource(LocationName, ProjectResource resource, …)` take the **entity** (set as `_resource`),
not a `ProjectResourceId`. `AssignedLocation.Create(ProjectResource resource, …)` likewise.

L2.3 **Invariant #5 moves in-aggregate (amends ADR-0030 §4).** With the entity in hand,
`Provider.AssignResource` compares `resource.ProviderName` to its own `Name` and rejects a mismatch — the
original ADR-0026 intent, now clean because the natural key (not the surrogate `ProviderId`) is on the surface.

L2.4 **The resource repository is keyed by the natural key.** `IRepository<ProjectResource, ProjectResourceKey>`
(`FindAsync(ProjectResourceKey)`); the DataAccess implementation resolves `ProviderName`→`ProviderId`
internally. Use-cases load the resource entity by key and pass it to the aggregate — they never see a surrogate.

L2.5 **Query contracts stop returning the surrogate.** `ResourceAssignmentTarget` drops `ProjectResourceId`
(the read-only-mode guard reads `resource.OperationMode` from the loaded entity; the cross-aggregate
non-shared-location check stays a `bool` query). `IResourceQuery.GetResourceIdAsync` and the id-returning
overloads are removed or re-typed to the key.

L2.6 **The sync event carries the natural key.** `ResourceAssignmentChangedEvent(ProjectResourceKey)` (was
`ProjectResourceId`); `IResourceAssignmentQuery.GetProjectsUsingResourceAsync(ProjectResourceKey)` resolves the
surrogate internally. (The F5 `ProjectResourceAssignmentChangedEvent` already carries `ResourceName`.)

After Level 2, the `ProjectResourceId` *type* still exists as the EF PK value object, but it lives only inside
`Core.DataAccess` mapping/query internals and the aggregate's shadow PK — **unreachable from `Core` use-cases,
from query contracts, and from any public Domain member.**

## Considered options

- **Composite as a provider-less `ProjectResource` row (rejected).** Modelling Project-as-resource as a special
  `ProjectResource` that points at a `Project` would leave the link untouched, but pollutes `ProjectResource`
  with provider-less rows and blurs "a resource is provider-owned". The discriminated link keeps
  `ProjectResource` clean.
- **Materialised snapshot of the source project's resources at assign time (rejected).** Stable, but the set
  goes stale when the source project changes; needs re-sync machinery. Lazy expansion always reflects current
  state.
- **One-level expansion only (rejected).** Simpler and cycle-free, but less general; the developer chose full
  Composite semantics, so recursion + cycle detection is the contract.
- **Keep communicating by surrogate `ProjectResourceId` (rejected as the boundary identity).** Convenient
  (the FK is right there) but couples aggregates to storage and violates ADR-0029; the surrogate stays for
  persistence only.
- **Level 2 via an `internal` id property instead of a navigation (rejected).** Making
  `AssignedLocation.ProjectResourceId` `internal` would not hide it from `Core.DataAccess`/`Core`
  (different projects) without `InternalsVisibleTo`, and query contracts would still surface it. The
  navigation + shadow-FK pattern (ADR-0031) is the only one that genuinely confines the surrogate to storage.
- **Keep ADR-0016's by-id reference + ADR-0043's client-generated id (rejected).** Reaching Level 2 needs the
  entity in the aggregate anyway; once it's there, the navigation derives the FK and the client-generated id
  becomes unnecessary — so the simpler, fully-encapsulated path supersedes both.

## Consequences

- Inter-aggregate communication standardises on `ProjectResourceKey`; the surrogate is confined to storage and
  read-query internals (ADR-0029).
- The composite is **additive** when built: one nullable FK + a CHECK, the `IResourceExpander` port, and
  read-side expansion. No change to the leaf path or to `ProjectResource` itself.
- **Expansion touch-points (future):** the read/sync surfaces that enumerate a project's resources —
  `ResourceDetailsQuery`, `ProjectDetailsQuery`, `HeappeAuditTargetQuery`, and the Waldur / HEAppE exporters —
  are where `ProjectSet` refs must be expanded. Listed here so the future feature's blast radius is known.
- **Cycle detection is mandatory** once `ProjectSet` ships; an un-guarded recursive expand would loop on a
  P1↔P2 assignment.
- `Project.Resources` gaining `ProviderName` is a small read-model change usable immediately (independent of the
  composite).
- **Level 2 blast radius (modular only):** `AssignedLocation` mapping (navigation/shadow FK), `Location`/
  `Provider` `AssignResource` signatures (entity), the resource repository key (`ProjectResourceKey`), the
  resource read/assignment query contracts, the assign/update/remove assignment endpoints, and
  `ResourceAssignmentChangedEvent` + its handler. **No schema migration** (column/FK/indexes unchanged). F4/F5
  modular suites must stay green (F5's project-link is already navigation/key-shaped, so it is barely touched).
  Legacy `src/Core` and the Waldur/HEAppE adapters are out of scope.
