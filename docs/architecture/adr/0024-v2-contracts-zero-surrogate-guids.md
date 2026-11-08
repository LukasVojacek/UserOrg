# ADR 0024 - v2 API contracts carry zero surrogate GUIDs; resources addressed by their unique name, keyless entities by a generated slug

- Date: 2026-06-22
- Status: accepted (2026-06-22) — resource model data-verified: resources are M:N to providers and uniquely named, addressed by `ResourceName`; no `ProviderName` column, no invariant #5. See "Data verification" for the evidence.
- Supersedes: the "Explicitly deferred" section of ADR-0022 (response ids, FK references, GUID list-filters)
- Relates to: ADR-0016 (resource-assignment writes), ADR-0018 (read-scope tiers / 404-hiding)

## Decision

No **surrogate database GUID** appears anywhere in a v2 API contract — not in a route, body, query
filter, or response. A v2 endpoint addresses and returns entities by one of three forms, in order of
preference:

1. **Natural key** — `providerName`, `projectShortName`, `tagName`, `idpUserId` (the IdP `sub`, for a User).
2. **Natural composite** — when one name is not unique on its own: `(providerName, resourceName)` for a
   Resource (the DB uniqueness key adds `ServiceName` — see below), `(providerName, locationName)` for a
   Location.
3. **Generated public slug** — an immutable, non-PK, human-citable reference for the entities with **no
   unique natural set** (a **Request**; an **AssignedLocation** allocation). Form: a readable prefix +
   random suffix, **not** derived from the surrogate `Id` — Request `req-{e3}-{projectShortName}-{r6}`
   (`e3` = first 3 alphanumerics of the email local part, lower-cased; `r6` = 6 random base36 chars);
   allocation `asg-{resourceName}-{r6}`.

This strengthens ADR-0022 (which removed surrogate *lookups* on three reads but deliberately deferred
response ids, FK references, and GUID filters during the migration). That deferral is now closed: the
no-GUID rule is universal across the v2 surface.

### Resources are addressed by their unique name (data-verified)

A `ProjectResource` is **shared M:N with Providers** — real data shows `OPEN-33-82` allocated under
IT4Innovations *and* LUMI — so it has no single owning provider. But **`ResourceName` is globally unique
among active resources**, so a resource is addressed by its name alone:

- Add a **filtered unique index `(ServiceName, ResourceName) WHERE IsDeleted = 0`** on `ProjectResource`
  (`ServiceName` contains a future cross-source name clash; the filter stops a soft-deleted row from holding
  the slot). **No `ProviderName` column, no `Provider.Name` alternate key, no writer changes.**
- `AssignResource` resolves `resourceName` directly to the single active resource — the provider and
  location come from the route — so there is **no chicken-and-egg** and nothing to record on the resource.
- **No invariant restricting a resource to one provider:** the M:N sharing is valid and must be preserved
  (enforcing "allocate only under own provider" would reject `OPEN-33-82`).

### Per-endpoint resolution

- **Resource** (`AssignResource`): drops the `ProjectResourceId` GUID body for `resourceName`, resolved
  against the filtered unique `(ServiceName, ResourceName)` index — works pre-assignment, no provider
  needed. `GetResource` is unchanged (its nested route already scopes the read).
- **Resource allocation** (`UpdateAssignment`, `RemoveAssignment`): an `AssignedLocation` has **no unique
  natural set**. Only a *non-shared* location makes `(provider, location, resource)` unique; on a *shared*
  location a resource may hold several allocations that are byte-identical in every natural field
  (aggregation, unit type, count, specs) and differ only by the surrogate `Id` (ADR-0016). A value-derived
  key therefore cannot be unique. It gains a generated **`asg-{resourceName}-{r6}`** slug, addressed as
  `Providers/{providerName}/Locations/{locationName}/Assignments/{slug}`; the slug is returned in the
  allocation list so the client addresses back by it. No runtime disambiguation, no ambiguity.
- **Request** (`AcceptRequest`, `RejectRequest`, `GetPublicRequestInfo`): no natural key, so it gains a
  generated **`req-{e3}-{projectShortName}-{r6}`** slug, addressed as `requests/{slug}`. Generated **in the
  v2 create use-case** (a pure helper — no trigger, no DB-side logic) and **nullable**: only v2-created
  requests have one. A v1-created request keeps a null slug and is handled through v1; a v2 endpoint
  addresses only v2-created requests and a slug miss returns 404 (optionally hinting "use the v1 endpoint").
  A **filtered unique index `WHERE Slug IS NOT NULL`** guards the rare `r6` collision and tolerates the nulls.
- **Filters & response refs** — convert to the referent's natural key (join-and-project in the read
  queries): `GetRequests` **and `GetSentRequests`** filters / `Summary` FK refs (`{Project,User,Provider}Id`);
  `UserSummary.AssignedProviderId`; `GetExtendedProject.AssignedTags` (`Guid[]` → tag names);
  **`GetResource`/`GetResources` response bodies** (resource `Id` + nested assignment `Id`/`ProjectResourceId`/
  `LocationId` — the route is natural-key but the body was not); `GetPublicRequestInfo`'s echoed `RequestId`;
  and self-`Id` fields in Tag/Location/CreateRequest/AssignResource responses.
- **`Location` / `Created` headers** must not embed GUIDs either: `AssignResource` (→ allocation slug),
  `AddLocation` (→ `Providers/{p}/Locations/{name}`; also fix the dead `GetLocationV2` route name),
  `CreateRequest` (→ `requests/{slug}`).
- **Internal ids stay:** `AssignedLocation.Id` / `ProjectResourceId` remain the canonical key for the HEAppE
  sync event, output-cache tags, and internal lookups — the slug / `resourceName` is an *additional* public
  handle, not a replacement.

## Context

- A natural-key audit of the v2 surface found every remaining contract GUID fell into: already-decided
  escape hatches (Request, resource allocation), or ADR-0022's explicitly-deferred set (response ids, FK
  refs, filters). The product requirement "no DB GUID in any contract" removes the escape-hatch option
  for everything that *can* be given an identity, and forces the deferred set into scope.
- The only entities with no natural identity are **Request** (an event-like record) and **AssignedLocation**
  (a keyless allocation; its `(LocationId, ProjectResourceId)` index is non-unique by design, ADR-0016).
  Both take a generated slug: a Request is event-like, and an `AssignedLocation` has no unique natural set
  (only non-shared locations make `(provider, location, resource)` unique; shared locations permit
  byte-identical duplicates). A value-derived slug would collide exactly where the rows do.
- `UserName` is mutable (tracks the IdP) and was rejected as a stored cross-aggregate key; User links keep
  their internal `UserId` FK and are addressed at the API by the immutable `IdpId`.

## Considered options

- **Resolve `(provider, name)` internally via the location path, no column (rejected).** Works for reads
  and already-assigned resources, but is circular for `AssignResource`'s first allocation — the operation
  that creates the very link the resolution depends on. Cannot satisfy a GUID-free `AssignResource`.
- **`ProviderId` surrogate FK on `ProjectResource` (rejected).** Reintroduces a surrogate relationship and,
  if stored alongside `ProviderName`, a 3NF transitive dependency. The natural-key column with a unique
  `Provider.Name` alternate key gives resolution and referential integrity without a surrogate.
- **`providerName__resourceName` slug column on `ProjectResource` (rejected).** Non-atomic (1NF), redundant
  with the nested route that already carries `providerName`, and lossy slugification risks collisions.
- **Hard `UNIQUE(location, resource, aggregation)` for allocations (rejected).** Would change legacy
  behaviour (legacy permits null/duplicate aggregations), can fail on existing data, and `aggregationName`
  is `nvarchar(max)` — not indexable without a length cap.
- **Value-derived slug for the allocation (rejected).** A slug built from `(location, resource, aggregation,
  specs)` is non-unique on shared locations — it collides precisely where the rows are duplicates. A unique
  slug must be *generated*, not derived.
- **Natural composite + runtime 409-disambiguation for the allocation (rejected).** Zero-migration, but
  ambiguous on shared-location duplicates (returns `409`), and presents an incomplete natural key as though
  it were whole. The generated slug is unambiguous and treats the two keyless entities uniformly.
- **Keep `AssignResource` on its GUID as a documented exception (rejected by product).** Cheapest (no
  migration), but violates the universal no-GUID rule. Recorded here as the path not taken.

## Consequences

- **One additive migration:** a filtered unique index `(ServiceName, ResourceName) WHERE IsDeleted = 0` on
  `ProjectResource`; nullable `Request.Slug` and `AssignedLocation.Slug` columns generated in the v2 create
  use-cases (**no trigger, no backfill**), each with a filtered unique index `WHERE Slug IS NOT NULL`. No
  `ProviderName` column, no `Provider.Name` alternate key, no invariant #5. Reconcile the `ResourceName`
  index collation before building.
- **Legacy is fully untouched and there is no trigger.** Slugs are generated only by the v2 create
  use-cases; v1-created rows keep a null slug and stay on v1. A v2 endpoint addresses only v2-created
  entities and returns 404 (optional "use v1" hint) otherwise — the simplest coexistence boundary, since
  v1 and v2 run side by side.
- **Reads stay on the existing location-path resolution** where the resource is already assigned; only the
  resource *owner* moves to the column.
- **No allocate-under-own-provider invariant:** data shows resources are legitimately shared across
  providers (`OPEN-33-82` under IT4Innovations and LUMI), so it is *not* enforced — it would reject valid rows.
- **`404`-hiding (ADR-0018) extends to the natural-key and slug lookups:** missing, wrong-provider, and
  out-of-scope return the identical `ProblemDetails`.
- **The allocation slug removes the ambiguity ceiling entirely:** because it is generated, even
  byte-identical duplicate allocations on a shared location are individually addressable — no
  `409 ambiguous` path exists, and no incomplete natural key is exposed.

## Independent review findings (2026-06-22)

Two independent reviewers checked an earlier draft against the code; their findings are now **folded into
the Decision above**. Retained here as the audit trail and the implementation checklist. Verified against
source where cited.

### A. Contract-GUID inventory was incomplete — add these

- `GetResource` / `GetResources` **response bodies** expose surrogate GUIDs (`ResourceResponse.Id` and,
  per assignment, `Id` + `ProjectResourceId` + `LocationId`). The route is natural-key but the body is not —
  this ADR wrongly treated the read as fully done.
- `GetSentRequests` (`GET Requests/Sent`) returns its own `Guid ProjectId/UserId/ProviderId` — a separate
  endpoint from `GetRequests`, must be fixed too.
- `GetExtendedProject` returns `AssignedTags: IReadOnlyList<Guid>` — should be tag **names**.
- `GetPublicRequestInfo` **echoes** `RequestId` (GUID) in its response body, not just the route.
- **`Location` / `CreatedAtRoute` headers** emit GUIDs on `AssignResource` (`.../assignments/{id}`),
  `AddLocation` (and it routes to a **non-existent** route name `GetLocationV2` — a latent bug), and
  `CreateRequest` (`v2/Requests/{guid}`, a route that does not exist). Headers are part of the contract.

### B. Migration is NOT additive-safe as written — blockers

- **`ProviderName` cannot be `NOT NULL` and has no derivable default.** Three writers insert
  `ProjectResource` standalone with no provider — legacy `ProjectResourceService.InsertProjectResourceAsync`,
  the **Waldur** sync (`WaldurExporter` / `WaldurProjectResourceContextExtensions`), and `ConvertToEntity` —
  and a resource can have **zero assignments**, so the backfill cannot derive a provider for every row. The
  column must be **nullable**, and every co-writer must set it or the row stays unaddressable by name.
- **`UNIQUE(ProviderName, ResourceName)` will likely fail to build.** `ResourceName` uniqueness was
  **already dropped** (migration `20260306155600`), and resources are partitioned by **`ServiceName`**
  (`LEXIS` vs `WALDUR`) — distinct rows legitimately share a name. The key almost certainly must include
  `ServiceName`, and needs a **pre-flight duplicate check on production data**.
- **`Provider.Name` is not unique today** and Waldur keys providers by `ExternalId`/`ServiceName`, so
  duplicate provider names may exist → the alternate key may fail. Pre-flight check required.
- **All new unique indexes must be filtered (`WHERE IsDeleted = 0`)** — soft-deleted rows otherwise hold
  their `(provider, name)` / slug slot and block re-creation (a behaviour change).
- **Slugs must be `PERSISTED` computed columns or trigger-backed**, not plain `DEFAULT`s — an EF/legacy
  insert that lists the column bypasses a default.
- **Collation mismatch:** `ProviderName` equality is `OrdinalIgnoreCase`, `ResourceName` is case-sensitive,
  the SQL index is case-insensitive (`*_CI_AS`). Reconcile before indexing.

### C. Keep surrogate ids internal — slug is additive, not a replacement

`AssignedLocation.Id` and `ProjectResourceId` are consumed internally by the HEAppE sync event
(`ResourceAssignmentChangedEvent`), output-cache tags (`CacheTags.ProjectResource`), and the created
`Location` header. The slug / `resourceName` is an **additional public handle**; the internal GUID stays
the canonical key for events, cache, and sync. (immudb audit usage could not be confirmed from code —
verify separately.)

### D. Invariant #5 has no enforcement point and data may already violate it

"Allocate only under the resource's own provider" is enforced nowhere today, and with provider *derived*
from assignments a resource assigned across two providers has a contradictory owner. Pre-flight-query for
cross-provider resources; choose a use-case enforcement point; quarantine violators before the unique index.

### Data verification (2026-06-22, populated DB) — resource model REVISED

The first two environments were empty. A third (`127.0.0.1,44985 / UserOrgDBContext`) is populated —
94 active resources, 165 allocations, 14 providers, 711 requests — and the diagnostics there **supersede
the `ProviderName`-column "enabling change" above and drop invariant #5**:

| Check | Result |
|---|---|
| `Provider.Name` duplicates | none → filtered unique alternate key OK |
| `(provider, name)` → multiple resources | none |
| **Cross-provider resources** | **`OPEN-33-82` is allocated under IT4Innovations *and* LUMI** |
| **`ResourceName` uniqueness (active rows)** | **globally unique** |
| Identical duplicate allocations | `Barbora GPU` ×2 (only the surrogate `Id` differs) |
| `ServiceName` split | resources all `LEXIS`; providers `LEXIS` (13) + `EFP-Core` (1) |
| Name incl. soft-deleted | one name reused across an active + a deleted row |

**Revised resource model (supersedes the `ProviderName` column and invariant #5):**

- **Resource↔Provider is M:N.** A resource is legitimately shared across providers (`OPEN-33-82` under
  IT4Innovations *and* LUMI), so a single `ProviderName` on the resource row is **wrong**. The
  `ProviderName` column, the `Provider.Name` alternate key, the legacy/Waldur writer changes, and
  **invariant #5 (allocate-only-under-own-provider) are all dropped** — enforcing #5 would reject this valid
  row.
- **`ResourceName` is globally unique among active rows**, so a resource is addressed by **its name alone**.
  `AssignResource` resolves `resourceName` directly — **no provider needed, no chicken-and-egg, no new owner
  column, no backfill**. Defensive key: a **filtered unique index `(ServiceName, ResourceName)
  WHERE IsDeleted = 0`** (so a future cross-`ServiceName` name clash is contained); resolve `resourceName`
  to the single active row, returning `409` only in the (currently non-existent) clash case.
- **Confirmed unchanged:** the **allocation slug** (identical duplicate allocations exist — a value-derived
  key is impossible) and the **Request slug** (no natural key). All new unique indexes are filtered
  (`WHERE IsDeleted = 0`); slugs are persisted-computed from `Id`.

**Net effect:** track ③ collapses from "`ProviderName` migration + legacy/Waldur writer changes +
invariant #5" to **one filtered unique index `(ServiceName, ResourceName)` + `AssignResource` taking
`resourceName`**. The Decision body's resource passages are to be rewritten to this model.
