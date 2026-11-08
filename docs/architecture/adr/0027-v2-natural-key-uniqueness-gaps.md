# ADR 0027 - v2 natural-key uniqueness gaps: filtered-unique remediation for tag, project-role, and provider name

- Date: 2026-06-27
- Status: accepted (2026-06-27) — live-data-verified against `UserOrgDBContext` @ 127.0.0.1,1433. Records three latent data-integrity gaps found during the ADR-0026 grilling and their dispositions.
- Relates to: ADR-0026 (ProjectResource per-provider — the first instance of this pattern), ADR-0007 (tags by name), ADR-0008 (Core owns project-role writes), ADR-0011/0012 (provider name as natural key, app-level uniqueness)

## Context — one recurring pattern

v2 addresses entities by **natural key** (name/slug) per ADR-0022/0024, but several underlying tables have
**no DB-level uniqueness** backing that addressing — uniqueness is assumed in app code or in the aggregate's
in-memory invariants. A data audit found **real violating rows** in three more places (in addition to the
resource case fixed by ADR-0026). Each is the same shape:

> v2 resolves `<entity>` by a natural key → the table allows duplicates → live data contains a duplicate →
> the v2 lookup/aggregate is ambiguous or inconsistent.

The uniform remediation has two parts: **(a)** the guard is enforced in the **domain model on every mutation
(with tests)** — see "Enforcement" below; **(b)** an **additive filtered unique index** (`WHERE IsDeleted = 0`,
plus a not-null filter where the column is nullable) as a **concurrency backstop**, preceded by a one-time
**pre-flight data de-dup**. Filtered (not full) because soft-deleted history must keep its old value. Additive
and legacy-safe. The DB constraint never replaces the aggregate guard.

## Decision

| # | Gap | Target identity | Remediation | Timing |
|---|---|---|---|---|
| 1 | **ProjectTag.Name** not unique; same tag name assignable twice to one project | **Global shared catalog, `Name` unique** (ADR-0007 "shared catalogue") | De-dup catalog, then unique index on `ProjectTag.Name` (filtered on soft-delete) | **F1** — together with ProjectTag update/delete + legacy tag-create decommission (legacy currently permits duplicate names) |
| 2 | **UserPermission** allows >1 active project role per (User, Project) | One active role per `(UserId, ProjectId)` (the ADR-0008 invariant, currently in-memory only) | De-dup (keep the newest role, soft-delete the older), then filtered unique `(UserId, ProjectId) WHERE ProjectId IS NOT NULL AND IsDeleted = 0` | **F3** — with admin role management + legacy role-write decommission (legacy can still create duplicates) |
| 3 | **Provider.Name** not unique (app-level only, ADR-0011/0012) | Provider name unique among **active** rows | Filtered unique `Name WHERE IsDeleted = 0` — **additive now**, no de-dup needed (active set is already unique) | **soon** — additive, unblocks the cross-aggregate natural-key reference below |

### Enforcement is aggregate-first; the DB index is only a backstop

The uniqueness **guard is enforced in the domain model on every mutation, with tests** — the filtered unique
index is a **concurrency backstop only**, never the primary guard, and **manual DB writes are not a runtime
path** (only the one-time data de-dup below touches rows directly).

| # | Guard | Enforced in | Tests |
|---|---|---|---|
| 1 | One catalog tag per `Name`; one tag-name per project assignment | **Use-case + domain port** (mint/assign resolves the catalog tag by name and rejects a second mint) — cross-aggregate, cannot be seen inside one aggregate | integration test on the mint/assign use-case; unit test on `Project` rejecting a duplicate-named assignment in its loaded tag collection |
| 2 | One active role per `(UserId, ProjectId)` | **`Project` aggregate** — `UpsertMemberRole` over the loaded `_memberRoles` (within the aggregate boundary, ADR-0008) | unit test on `Project` directly (no mocks) |
| 3 | Provider name unique among active | **Use-case + domain port** (create/rename provider queries for an active name clash and returns `Error`) — cross-aggregate | integration test on the create-provider use-case |

Cross-aggregate uniqueness (#1, #3, and ADR-0026's resource name) genuinely cannot live inside a single
aggregate — the project's standard pattern applies (CLAUDE.md): the **use-case** does the async resolution
through a **domain-owned port**, then passes the result into a **synchronous aggregate method**. The DB
filtered-unique index catches only the concurrent-writer race the app check cannot.

### Cross-aggregate FK natural-key references (additive)

The persisted cross-aggregate FKs (`Project.CreatedBy`, `User.AssignedProviderId`, `Request.{Project,User,
Provider}Id`, `AssignedUserToProject.*`) remain surrogate **GUIDs** for legacy. Where v2 wants a natural-key
reference, take the **additive path**: add a nullable natural-key column + a **filtered unique index** on the
target, and resolve the reference **in the use-case** (app-level) — **not** a strict EF alternate key / FK.

Rationale: an EF alternate key needs a **full** unique constraint, which **soft-deleted duplicates break**
(e.g. the soft-deleted `DLR` row). A filtered unique index is soft-delete-safe but cannot back an FK, so the
reference is resolved in code (which v2 already does). User references must target the **immutable `IdpId`**,
not `UserName` (UserName is mutable and non-unique — 369 distinct of 371).

## Consequences

- **All remediations are additive** (new filtered unique indexes; new nullable columns where a reference is
  added). No NOT-NULL tightening, no type changes, legacy keeps working.
- **De-dup is a pre-flight gate** for #1 and #2 (real violators exist). #3 needs no de-dup (active set clean).
- **Timing is gated by legacy writers:** #1 and #2 can only get their DB constraint once the legacy
  create/role-write paths that produce duplicates are retired (F1 / F3). #3 is safe immediately.
- **Manual DB writes are one-time remediation only.** The de-dup runs once against existing legacy-created
  bad rows; it is **not** a runtime path. All ongoing writes go through the aggregate/use-case guards above,
  so the violations cannot recur once the legacy writer is retired (F1/F3).
- This ADR is a **tracking record**; each item is implemented in its phase. The resource instance (the
  fourth of this pattern) is already done in **ADR-0026**.

## Data verification (2026-06-27, populated `UserOrgDBContext` @ 127.0.0.1,1433)

- **Tags:** 10 rows, 8 distinct names. Duplicates: `acsac` ×2 (both unassigned/orphan), `DraTag2` ×2 **both
  assigned to project `EverestGeneral`** → ambiguous `tags/{tagName}`. **No tag is assigned to more than one
  project** (the catalog M:N is not exercised — duplicates are bugs, not legitimate sharing).
- **Project roles:** user `2844CD19…` holds **two active roles** (`SystemRoleId` 4 and 2, both `IsDeleted=0`)
  on project `5457044E…`. `AssignedUserToProject` and `AssignedProjectResourceToProject` are clean (composite PKs).
- **Provider name:** `DLR` ×2 — one **active** (1 location) + one **soft-deleted** (0 locations); 14 providers,
  13 distinct names. Active set is unique → a filtered unique index builds cleanly with no de-dup.
- **User key:** `IdentityProviderUserId` is unique (371/371); `UserName` is not (369/371) → natural-key
  references to User use `IdpId`.

## Amendment (2026-09-29): provider name twins

`IX_Provider_Name` is unique among active rows only (`[IsDeleted] = 0`), so legacy data can hold **name
twins**: several `Provider` rows with one `Name`, at most one active (`DLR` above). They are legacy data, not
merged or migrated. A by-name write (`IProviderRepository.FindIncludingDeactivatedAsync`, used by Reactivate and
Deactivate) resolves them by the **twin resolution order**: the active row first, then `ModifiedAt` descending
(NULL last), then `CreatedAt` descending, then the shadow `Id`. `GET api/v2/ProviderAdmin/Providers` with
`includeInactive=true` lists every twin, ordered by `Name` and then the same order, so the row a by-name write
acts on comes first. Both use `ProviderTwinOrder` (`src/Modules/Core.DataAccess/Configurations/`), so they
cannot drift. Reactivate on a name held by an active twin answers 409 `provider.not_deactivated`; Create refuses
a name any twin holds (`ProviderDetailsQuery.ProviderExistsAsync`).
