# ADR 0023 - v2 optimistic concurrency for mutable aggregates via `If-Match` / rowversion

- Date: 2026-06-16
- Status: accepted

## Decision

Mutable v2 aggregates that are exposed to concurrent edits adopt **opt-in optimistic concurrency** using HTTP
conditional requests, not blanket on every write:

- The aggregate carries a **rowversion concurrency token** mapped as an **EF shadow property**
  (`IsRowVersion()` in the EF configuration), so the **domain entity stays persistence-ignorant** — no version
  field is added to the aggregate's own type (consistent with the entity-purity rule and ADR-0010's
  "EF config adapts to the entity" stance).
- The aggregate's **single-item GET** emits a **strong `ETag` derived from the rowversion** (not the body
  hash used by ADR-0020's read-side revalidation), giving the client a precondition token to send back.
- The **mutating endpoint requires `If-Match`**: the supplied token is set as the rowversion's original value;
  a stale token surfaces as `DbUpdateConcurrencyException` and is translated to **`412 Precondition Failed`**
  (`ProblemDetails`, code `concurrency.precondition_failed`). A **missing** `If-Match` on a concurrency-guarded
  write is **`428 Precondition Required`**.
- This is applied **only** to aggregates where lost-update is a real risk — initially `UpdateProject`,
  `ChangeProjectUserRole`, and `UpdateProvider`. Other writes keep their current behaviour.

## Context

ADR-0020's read-side ETag (`ConditionalGetMiddleware`, a body hash) answers `If-None-Match` for bandwidth, but
a body hash is unsuitable as a **write** precondition: it depends on projection, read-scope trimming, and
serialization order, so it does not stably identify the persisted version a client edited. A management
console has overlapping editors (two managers changing the same project or a member's role), where a
last-write-wins silent overwrite is a real data-loss path. HTTP already standardises the fix — `If-Match`
against a strong validator — and EF Core already implements the server half via concurrency tokens.

## Considered options

- **Opt-in `If-Match` + rowversion (chosen).** Standard HTTP semantics, EF-native enforcement, zero cost on
  writes that don't opt in. Cost: a shadow rowversion + migration per guarded aggregate, and the GET must
  expose a version-derived ETag distinct from the body-hash one.
- **Blanket optimistic concurrency on every write.** Rejected: forces a token round-trip on writes with no
  contention (idempotent assigns, single-actor commands), and complicates every endpoint for a risk most do
  not have.
- **Reuse the body-hash read ETag as the write precondition.** Rejected: not a stable version identifier
  (see Context); a scope-trimmed or reordered projection changes the hash without the entity changing.
- **Last-write-wins (status quo).** Rejected: silent lost updates on concurrently-edited aggregates.

## Consequences

- Each guarded aggregate gains a shadow rowversion column (a migration) and its single-GET emits a strong,
  version-based `ETag`; the body-hash `.WithETag()` revalidation (ADR-0020) still applies for `304` on the
  same endpoint, so a guarded GET may carry the version ETag specifically.
- Write endpoints gain an `If-Match` requirement and two new failure codes (`412`, `428`); contract tests
  assert the stale-token and missing-token paths.
- `ETag` is exposed via CORS `WithExposedHeaders` (shared with ADR-0020).
- The rowversion is internal persistence state; it does not appear in the domain model or in request/response
  DTOs other than as the opaque `ETag`/`If-Match` token.
