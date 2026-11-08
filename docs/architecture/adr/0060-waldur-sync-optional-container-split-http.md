# Optional: Waldur.Sync as a separate container over HTTP (pre-RabbitMQ)

**Status:** proposed — optional deployment variant, decision deferred · relates to ADR-0053, 0055, 0056

## Context

ADR-0053 keeps the rewritten integration in the monolith but cuts it at the
`IWaldurEventPublisher` port. We are **not decided** whether the pull side stays in-process or
runs as its own container, and we explicitly do **not** want RabbitMQ yet — only abstractions
that make a later broker adoption cheap. This ADR describes how the container split works with
plain HTTP, so the decision can be taken (now or later) without re-architecting.

Two things cross the boundary in the monolith and must survive the split:

1. **Events out:** `IWaldurEventPublisher.PublishAsync` (synchronous ack, ADR-0056).
2. **State in (startup only):** the cold rebuild of the SHA map reads current UserOrg state
   (ADR-0055) via the `IShaMapRebuilder` port — the sync module never references the DB.

## Decision (if/when the split is taken)

**Composition changes; core code does not.** `Waldur.Contracts`, the whole of `Waldur.Sync`
(worker, diff, hasher, API client) and the consumer handlers stay byte-identical. Only the
composition roots and two adapter implementations differ:

- **New host `Waldur.Sync.Host`** — a minimal worker host (ServiceDefaults/OTel as usual)
  registering `AddWaldurSync(...)` plus the HTTP adapters below. Added to `UserOrg.AppHost` as
  an Aspire resource with a service reference to the API; `Waldur:Instances` configuration moves
  to this container.
- **`HttpWaldurEventPublisher : IWaldurEventPublisher`** — POSTs the serialized event to an
  internal endpoint on userorg-api: `POST /internal/waldur-sync/events`. The API dispatches to
  the same in-process handlers and returns 200 **only after the handler completed** —
  HTTP response = ack, so ADR-0056 semantics (SHA written only after success, retry next
  iteration) hold unchanged. Non-2xx or timeout = not acked. Retry/backoff via the standard
  resilience handler; no outbox, no queueing — the pull loop already is the retry mechanism.
- **`HttpShaMapRebuilder : IShaMapRebuilder`** — on startup calls
  `GET /internal/waldur-sync/state?instance={name}`, which returns the breadcrumb list
  `(entityType, externalId, userId?, sha)`. The **API side** computes the hashes (DB → DTO →
  `WaldurPayloadHasher`), reusing the exact hasher from `Waldur.Contracts` — one hash
  implementation, referenced by both containers, keeps the round-trip guarantee (ADR-0055) and
  the payload mapping code on the side that owns the DB. The response is small (one hash per
  item), so startup cost is one HTTP call, not a data export.

**Endpoint hardening:** both endpoints are internal service-to-service only — not exposed on the
public surface, authenticated with a shared API key or mTLS per platform convention, and
rate-irrelevant (one caller). They live in a dedicated `/internal/waldur-sync/*` group so the
future RabbitMQ consumer can replace them wholesale.

**What this buys even without RabbitMQ:** the original separation goals — sync log noise and
resource usage isolated in its own container (own OTel stream, own lifecycle/restarts, no
interference with API workloads) — are achieved with HTTP alone.

**Path to RabbitMQ from here:** replace `HttpWaldurEventPublisher` with a queue publisher and
host the handlers behind a queue consumer in the API (ack semantics shift as already anticipated
in ADR-0056: SHA on broker confirm, consumer-side retry, idempotent handlers absorb duplicates).
The state endpoint can stay HTTP — startup rebuild is request/response by nature and gains
nothing from a broker.

## Consequences

- The monolith⇄container decision becomes a **deployment choice**, reversible either way: two
  small adapter classes + one host project + Aspire wiring (~1 day), no core changes.
- New failure mode: API unreachable → publish fails → items retry next iteration (already the
  designed behaviour); a down API pauses sync progress rather than corrupting state.
- Event payloads become a wire format when split: keep them JSON-serializable and additive-only
  (no breaking field changes without versioning) from day one — cheap discipline in the monolith,
  mandatory after the split.
- Slight duplication of deployment surface (one more container image/resource) — the price of
  isolation.

## Amendments (independent review 2026-07-06)

### A1 — No automatic retries on the event POST

**Overrides "Retry/backoff via the standard resilience handler" above.** A resilience-handler
retry would re-POST while the first handler may still be executing (Heappe makes handlers slow) —
two DI scopes concurrently upserting the same rows, racing on unique constraints. Rule: the event
POST is sent **once**; any failure — including timeout — is simply "not acked" (no SHA write),
and the pull loop is the retry, exactly as in-process (ADR-0056). Additionally:

- the HTTP timeout must exceed the worst-case handler runtime (Heappe ensure chain included) —
  size it from measurements, not defaults;
- a timeout after the handler actually completed server-side means the next iteration runs the
  handler again — acknowledged and safe (idempotent handlers, ADR-0057);
- if unique-violation races between a still-running duplicate and a replay ever surface, the
  permitted fix is per-`externalId` serialization on the API side (or the in-handler
  retry-once rule from ADR-0056/A3), not POST retries.

