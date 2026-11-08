# ADR 0046 - Self-onboarding gets its own aggregate and table, split off the shared Request

- Date: 2026-06-30
- Status: accepted
- Supersedes: ADR-0009 (for the self-onboarding **persistence** model only)
- Relates: ADR-0019 (atomic accept), ADR-0047 (new-table migration authoring), ADR-0048 (endpoint shape)
- Issue: [work item #228](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/228)

## Decision

v2 self-onboarding (a user asking to join a project as a member) moves to a **dedicated
`OnboardingRequest` aggregate persisted in its own `OnboardingRequest` table**, instead of
riding the shared `Request` aggregate/table.

`OnboardingRequest` is a **standalone aggregate**, not an entity inside `Project`, and its domain
model is **free of persistence ids** (no surrogate `Guid`, no `ProjectId`/`UserId`). See the two
subsections below for the reasoning; the table that backs it follows.

### Why an aggregate, not a `Project` entity

- **Lifecycle & ownership.** An onboarding request is created by a **non-member** (someone with no
  role on the project) and exists independently of the project's own state. `Project` needs none of
  its invariants to reference pending requests, so it does not *own* them in the aggregate sense.
- **Aggregate size & contention (small-aggregate rule).** Requests accumulate unboundedly over a
  project's life and arrive concurrently from outside. Nesting them in `Project` would force loading
  and locking the whole `Project` root (members, tags, resources, *and* every request) just to file,
  list, or reject one — bloating the root and serialising unrelated writes on it.
- **Reference by identity.** The aggregate references `Project` by its natural key and the requester
  by theirs — the textbook "reference other aggregates by identity, not by holding them" rule.
- **The one pull the other way, declined.** If onboarding were a `Project` entity, *accept* would be
  a single-aggregate invariant (no two-aggregate transaction). But that strong-consistency need is
  already handled — and bounded — by ADR-0019's deliberate two-aggregate single-transaction accept.
  It does not justify collapsing every create/list/reject into the `Project` root.

### Pure domain model — natural keys only, no surrogate ids

The `Core.Domain` aggregate holds **only domain identity**, per ADR-0029 (natural keys at aggregate
boundaries) and the project's "entity free of DB concerns" rule:

```
OnboardingRequest (aggregate root)
  Project       : ProjectShortName     // natural key — NOT a Guid ProjectId
  Requester     : IdpId                // the requester's identity-provider id (natural)
  ContactEmail  : Email                // for notifications
  State         : OnboardingRequestState   // Pending → Accepted | Rejected (terminal)
  Slug          : OnboardingRequestSlug?   // GUID-free public id (ADR-0024)

  static Create(ProjectShortName, IdpId requester, Email, slug)  → raises OnboardingRequestCreated
  Accept()  : Result<Error>   // state-machine guarded; raises a transition event
  Reject()  : Result<Error>
  Delete()  : Result<Error>   // soft-delete (ISoftDeletableEntity)
```

The surrogate `Guid` primary key and the `Guid` foreign keys are a **persistence concern only**: in
`Core.DataAccess` the `Id` is an EF **shadow** property **assigned a Guid v7** (`Guid.CreateVersion7()`)
by the write-adapter on add — mirroring `DatabaseContext.ProjectTagHandler` (`entry.Property("Id")
.CurrentValue = Guid.CreateVersion7()`), **not** a DB `NewId()` default. The `ProjectId`/`UserId` FK
columns are **shadow** FKs the write-adapter resolves from the natural keys via the existing
directory ports (`IProjectDirectory.FindIdAsync`, `IUserDirectory.FindUserIdAsync` — already called
by the create use-case). EF config adapts to the entity; the entity never adapts to EF.

Other model facts: it carries **no** `RequestType` discriminator, **no** resource/provider fields,
and **no** action links — those belonged to the conflated `Request` model.

### Concrete table — `OnboardingRequest` (modular `Core.DataAccess`)

The table is a **persistence concern** — its surrogate `Id` and `Guid` FKs do **not** appear on the
domain model (above); they are EF shadow members resolved from the natural keys.

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK `PK_OnboardingRequest`, **app-assigned Guid v7** (`Guid.CreateVersion7()`), **no DB default** — EF **shadow** (not on model) |
| `ProjectId` | `uniqueidentifier` | no | FK → `Project(Id)` — **shadow** FK, resolved from `ProjectShortName` via `IProjectDirectory` at write time |
| `UserId` | `uniqueidentifier` | no | FK → `User(Id)` (requester) — **shadow** FK, resolved from `IdpId` via `IUserDirectory` |
| `ContactEmail` | `nvarchar(256)` | yes | requester email for notifications |
| `State` | `int` | no | default `0` Pending; `1` Accepted, `2` Rejected (terminal) |
| `Slug` | `nvarchar(120)` | yes | GUID-free public id (ADR-0024); returned by GET, used by accept/reject |
| `IsDeleted` | `bit` | no | default `0`; global query filter `!IsDeleted` |
| `CreatedAt` | `datetime2(3)` | no | default `SYSUTCDATETIME()` (shadow) |
| `ModifiedAt` | `datetime2(3)` | yes | shadow |

**Relationships / FKs** (both `OnDelete(NoAction)`, required, mirroring `RequestConfiguration`):
- `FK_OnboardingRequest_Project_ProjectId` → `Project(Id)`
- `FK_OnboardingRequest_User_UserId` → `User(Id)`

**Indexes:**
- `PK_OnboardingRequest` (`Id`)
- `IX_OnboardingRequest_Slug` — **unique, filtered** `WHERE [Slug] IS NOT NULL`
- `IX_OnboardingRequest_ProjectId_State` — supports the per-project received list
- `IX_OnboardingRequest_UserId_State` — supports the caller's sent list

No `RequestType`, `ProjectResourceId`, `ProviderId`, `ProjectResourceName`, or `ActionLink` columns.
- The legacy `Request` aggregate and table are **untouched**. They keep serving (a) legacy v1,
  (b) the deferred `ProjectResourceAssignment` flow, and (c) the existing v2 generic `requests`
  endpoints, which stay in place during the transition (ADR-0048).
- Accept stays **atomic across two aggregates in one transaction** exactly as ADR-0019
  prescribes: `OnboardingRequest.Accept()` + `Project.AssignUserWithRole(...)` then a single
  `SaveChangesAsync`. Both aggregates live in the same modular `Core.DataAccess.DatabaseContext`,
  so the atomicity guarantee is unchanged; ADR-0019 continues to govern the accept path.
- **No data backfill.** New v2 onboarding requests are written to the new table; in-flight rows
  in the old `Request` table continue to be handled by the kept-alive generic v2 / legacy v1
  endpoints. Two coexisting write paths to "join a project" are accepted (see Consequences).

## Context

- The originating issue's first draft (decision #2) kept the `Request` table and changed only
  the HTTP surface. During planning the decision was revised: a dedicated table better reflects
  that **onboarding and resource-assignment are two different concepts** that the `Request`
  aggregate had conflated behind a `RequestType` int and a pile of nullable resource/provider
  columns. Splitting them removes the discriminator and the dead columns from the onboarding
  path entirely.
- The action-link machinery on `Request` (`ActionLinkGenerator`, the `ActionLink` child,
  `HandleActionLink`, `GetPublicRequestInfo`) is **not reachable for onboarding in v2**:
  `MailNotificationSender` never emails the minted tokens, and `GetPublicRequestInfo` gates on
  `RequestType == 2` (ProjectResourceAssignment), so it never serves onboarding (type 1). The
  onboarding accept/reject are done by an authenticated Project Owner/Manager from the UI. The
  new aggregate therefore omits action links; the existing action-link endpoints stay as-is on
  the old `Request` flow (ADR-0048).
- There is effectively **one consuming UI application**. When it migrates it will switch wholly
  to the v2 onboarding endpoints, so a transition window with two write paths is low-risk and
  bounded.

## Considered options

- **Dedicated `OnboardingRequest` aggregate + table (chosen).** Clean bounded concept, no
  discriminator, no nullable resource columns, no action links. Cost: a new table + migration
  (ADR-0047) and a transitional second write path.
- **Keep self-onboarding on the shared `Request` table (issue decision #2, ADR-0009).** Smaller
  diff, no migration. Rejected: keeps the conflated model and the dead action-link/resource
  columns on the onboarding path, and blocks the clearer two-concept separation the consumer
  wants.
- **Backfill existing `Request` self-onboarding rows into the new table.** Rejected: unnecessary
  given the single UI consumer migrates wholesale and the old endpoints stay live to drain
  in-flight rows; backfill adds migration risk for no observable benefit.

## Consequences

- Two coexisting "join a project" write paths during the transition (old generic v2 `requests`
  + new `onboarding-requests`). This is deliberate and bounded; the old surface is removed in a
  follow-up once the UI has migrated (ADR-0048). Reviewers should not "fix" the duplication by
  deleting the old path inside this work.
- ADR-0009's *self-onboarding-only* migration intent still holds; only its *persistence target*
  changes (Request table → OnboardingRequest table). ADR-0009 is otherwise unchanged for the
  resource-assignment deferral.
- ADR-0019 (atomic two-aggregate accept, last-write-wins, no rowversion) carries over verbatim to
  `OnboardingRequest` + `Project`.
- The new table's migration is created only in the refactored `Core.DataAccess` project (ADR-0047).
- Follow-up issue: retire the old generic v2 `requests` self-onboarding endpoints after the UI
  cutover, and decide the long-term fate of the `Request` table's self-onboarding rows.
