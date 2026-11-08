# Waldur integration events are fat CRUD events (event-carried state transfer)

**Status:** accepted (grilling session 2026-07-06) · **amended after independent review 2026-07-06 — see Amendments** · relates to ADR-0053, ADR-0055, ADR-0061

## Context

The refactor cuts the pipeline at the point where today `DataConsumerBackgroundService` calls
`IExporter` with `ProjectDto`/`UserDto`/`ProviderDto` aggregates. The event contract crossing that
seam decides how well the future RabbitMQ split works: thin notification events ("id changed,
fetch it back") create a request/reply loop and re-couple the two sides; fat events carry
everything the consumer needs.

Deletion is a special hazard: the current code never deletes from pull absence (only Waldur's
`is_removed` flag exists), and a false `ProjectDeleted` emitted because of a paging bug or a
partial fetch would be the most damaging failure of the whole integration.

## Decision

**Events carry the full payload** the consumer needs (event-carried state transfer). The consumer
never calls back into `Waldur.Sync`. A future "Waldur pushes an event" capability is modelled as a
second *trigger on the producer input side* (event → targeted fetch → diff → emit), never as a
pull-back channel for the consumer.

**Taxonomy** (all events also carry `Instance`):

| Event | Payload | Emitted when |
|---|---|---|
| `ProviderCreated` / `ProviderUpdated` | provider (customer) data | id unknown / SHA differs |
| `ProjectCreated` / `ProjectUpdated` | project **including its marketplace resources** (+offerings, EFP storage mapping) | id unknown / SHA differs |
| `ProjectDeleted` | project id | **only** an explicit `is_removed`-style signal from the API |
| `UserAssigned` / `UserRoleChanged` | project id + user data + role | (project, user) unknown / SHA differs |
| `UserRemoved` | project id + user id | user missing from a **successfully fetched, complete** member snapshot |

- **Resources have no independent lifecycle** on our side, so they travel inside project events
  instead of having their own event stream. EFP/Puhuri differences stay inside the payload.
- **Project membership is diffed per user** (key `(instance, projectId, userId)`), because
  assignment/role-change/removal are exactly what the integration must propagate individually.
- **Delete-from-absence is forbidden for projects/providers** — absence from a pull is logged as
  a warning, nothing more. `UserRemoved` is the deliberate exception: `list_users` for a project
  is an authoritative complete snapshot, so *if and only if* that call succeeded, a missing user
  is an explicit fact. If the member fetch for a project fails, **no** membership events are
  emitted for that project in that iteration and its member SHA entries are left untouched.

## Consequences

- Consumers are idempotent by construction (upsert semantics), which the ack model (ADR-0056) and
  the future at-least-once RabbitMQ delivery both rely on.
- The producer must diff member lists (not just hash them as one blob); the SHA map holds one
  entry per project member (ADR-0055).
- Tombstone/absence-based cleanup (delete after N missing iterations) is intentionally out of
  scope; it can be added later without changing the contract.

## Amendments (independent review 2026-07-06)

The review verified the contract against the actual exporters and found four corrections. These
**override** the corresponding rows of the original decision table.

### A1 — `ProjectDeleted` is dropped; removal is a payload flag

`is_removed` exists only on `ApiProject` and today maps to `IsDeleted=true` inside the ordinary
upsert. A separate id-only `ProjectDeleted` event has no meaningful hash, which creates a
map-semantics trap: Waldur keeps returning the removed project, so deleting the map entry makes
the next iteration see "unknown id" and emit `ProjectCreated` — a delete/create ping-pong every
iteration. Instead:

- `ProjectPayload` carries **`IsRemoved: bool`**, included in the hash. Removal in Waldur is
  therefore just another `ProjectUpdated` (hash changed); the upsert handler soft-deletes
  (`IsDeleted = true`) when the flag is set. The map entry lives on and stabilizes.
- A project with `IsRemoved = true` is **exempt from the ≥1-eligible-resource rule** (A4) — its
  payload may carry zero resources. (Deliberate improvement: today removed projects are usually
  skipped by eligibility and the flag never lands in the DB.)
- Removed projects emit **no membership events**; existing memberships are left in place (as
  today). The SHA cold rebuild **includes** soft-deleted projects (ADR-0055), so restarts do not
  re-emit them.
- Event taxonomy shrinks to: `ProviderCreated/Updated`, `ProjectCreated/Updated`,
  `UserAssigned/UserRoleChanged/UserRemoved`, plus `StorageConfigurationChanged` (ADR-0061).

### A2 — Membership identity is `UserName`, not the Waldur user UUID

The DB stores no Waldur user UUID (`UpsertUserAsync` keys on `UserName` and writes no
ExternalId), so a UUID-keyed membership map could never be cold-rebuilt — a user removed while
the service was down would stay a member forever. Therefore:

- membership map key = `(instance, projectExternalId, userName)`;
- `UserRemoved` payload = `{ Instance, ProjectExternalId, UserName }`;
- the rebuilder emits one map entry per non-deleted `AssignedUserToProject`;
- `UserRemoved` replay against an already-absent assignment is a **required no-op**.

### A3 — `ProviderPayload` embeds locations derived from offerings

Provider locations are not raw API data: today `ProviderConsumer` fetches
`/api/marketplace-public-offerings/?customer_uuid=…` per customer and derives locations by
grouping offerings by SSH-login URL (case-insensitive), naming by longest common prefix trimmed
of `-`, stripping the `ssh://` scheme, mapping `archived` → `IsDeleted`, and skipping offerings
without an SSH endpoint. This derivation moves into **producer mapping** and its result — the
location list — is part of `ProviderPayload` and its hash; the Providers phase therefore includes
the per-customer offerings fetch.

### A4 — Eligibility rules are part of the contract, not an implementation detail

Mirroring today's behaviour (`ProjectConsumer` skips + `ProjectSyncSkipRegistry` suppression):

- a **resource** is eligible iff it has allocations and an `EffectiveId` or `BackendId`;
- a **project** is eligible iff it has ≥1 eligible resource **or** `IsRemoved = true` (A1);
- an ineligible project emits **no events at all — including membership events** — and is
  logged per ADR-0058;
- a project whose offering/resource detail fetch failed is treated as fetch failure: no events,
  no SHA writes, warning (same rule as the member-snapshot rule above).

