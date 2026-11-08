# Audit-log PoC: log-only sink now, immudb stays the target — narrows ADR-0004

Status: accepted — narrows [ADR 0004](0004-persistence-sql-immudb.md) (`accepted`; its SQL half
is fully implemented, its immudb half has never been written to) for the duration of this PoC.
**§2 (the log-only sink) is superseded and §4 (fail-closed for P1) is narrowed by
[ADR 0096](0096-audit-log-ships-sealed-to-immudb-through-an-unsealing-proxy.md)** — the PoC's
interim ended: `LoggingAuditSink` is gone, records are sealed and delivered to immudb
asynchronously, and a fail-closed P1 call site now guarantees only that the record was spooled.
§1, §3 and §5 are unchanged.

## Context

ADR-0004 committed to immudb as the audit store back when no audit code existed at all. That
commitment has stood unexecuted since: the immudb and immugw containers exist only for local
development — `UserOrg.AppHost/ImmuDbExtensions.cs` marks both `ExcludeFromManifest()`, so no
deployed environment has an immudb at all — and nothing has ever written to one, in any
environment: there is no `IAuditEventSink` implementation targeting immudb anywhere in the repo.
Adopting immudb for audit is therefore a deployment change, not merely a code change. This block
introduces the first real audit call sites — `AuditEvent`, `IAuditLogger<T>`, `IAuditEventSink`
(from `StepUpLogging`, wired in B01) — instrumenting exactly three P1 operations end to end: a
project role change, the anonymous resource-request action path, and the gateway's authorization
denial. Project membership changes (`AssignUserWithRole` / `UnassignUserWithRole`) are deliberately
left uninstrumented — three sites are enough to validate the record shape, which is what this PoC is
for. Standing up immudb integration and the
audit model at the same time would validate neither in isolation: a bad `AuditEvent` shape
discovered after wiring immudb means redoing the immudb work too.

The transactional-outbox pattern (ADR-0028) is the existing "reliable async side-effect"
mechanism in this codebase, and would be the obvious place to route audit events from — except
the outbox lives in the SQL `DatabaseContext`, and decision 1 below rules that database out as
an audit transport entirely. The outbox is therefore not available to this PoC regardless of
sink choice.

## Decision

1. **Audit records never land in the userorg SQL database.** immudb remains the ADR-0004
   target store; this is not up for revisiting here. Nothing in this PoC writes an audit event
   to `DatabaseContext` or any SQL-backed table, not even as a staging step.
2. **This PoC ships a log-only sink**, not immudb. `LoggingAuditSink` (B02) implements
   `IAuditEventSink` by emitting one `[AUDIT]`-prefixed structured log line per event. This is
   a deliberate intermediate step: it exercises the `AuditEvent` model against real call sites
   (B06, B07, B08) so the model is validated by the time immudb integration is built, rather
   than co-designed blind alongside it. It is not a fallback or a "temporary until immudb is
   convenient" excuse — it is how this PoC gets signal cheaply.
3. **Consequence of (1): the outbox is not an audit transport.** SQL-backed reliable delivery
   (ADR-0028) cannot carry audit events without contradicting decision 1. Audit events are
   therefore emitted explicitly in handlers via `IAuditLogger<T>`, alongside — not through —
   the business write. This also means **audit cannot be atomic with the business operation**:
   there is an accepted loss window between the business transaction's commit and the audit
   write (process crash, sink failure). No compensating mechanism is introduced in this PoC to
   close that window.
4. **Criticality is fail-closed for P1, fail-open for P2, decided at the call site.** For P1
   operations — role changes, membership, approvals — a sink failure propagates and the request
   fails visibly; P2 operations swallow it and succeed. Fail-closed is **not** a rollback: per
   decision 3 the audit write follows the already-committed business transaction, so the
   mutation stands even when the response reports failure. What it guarantees is only that an
   unaudited P1 change cannot pass silently — the caller sees the failure. The residual loss
   window stays open either way: a crash between the commit and the audit write leaves a
   committed mutation with neither an audit record nor a failed response. Because a fail-closed write
   can throw, the audit call is the **last** statement of the handler, after every side effect the
   response's correctness depends on (output-cache eviction above all): the accepted consequence is a
   committed change behind a failed response, never a committed change still served from cache. This split is not
   encoded in `AuditEvent` or `IAuditEventSink` — it is a decision each instrumented handler
   makes for itself, because criticality is a property of the operation, not of the audit
   record.
5. **`Data` carries identifiers only — with no exceptions.** No e-mail and no name goes into an
   audit event's `Data`, and any action token in `Data` is its hash, never the plaintext. The
   reason is immudb's append-only nature (ADR-0004): once a record lands there, a GDPR erasure
   request cannot be honoured for it — so nothing erasure-sensitive is written in the first
   place, regardless of which sink (log or immudb) is active today. The anonymous action-link path
   (`HandleResourceRequestActionEndpoint`, ADR-0064) is where this bites: possession of the token is
   the only thing ever known about who clicked it, so that record names no approver at all rather
   than substituting the PI's e-mail for the missing identifier. An `"action-link"` actor type
   without an actor identity is the honest record; recording the person by e-mail is not worth an
   unerasable one.

## Consequences

- ADR-0004 remains `accepted` as the long-term target; this ADR does not revoke it, only
  records that the interim state — audit events observable solely as `[AUDIT]` log lines, with
  no tamper-evident store behind them — is deliberate and time-boxed to this PoC, not a silent
  drift from the original decision.
- Until immudb integration lands, audit data has no durability or tamper-evidence guarantee
  beyond whatever the log pipeline already provides; it must not be treated as satisfying any
  compliance requirement ADR-0004 was written for.
- The next step — replacing `LoggingAuditSink` with an immudb-backed `IAuditEventSink` — is
  additive: `AuditEvent`, `IAuditLogger<T>`, and every call site instrumented under this PoC
  are expected to carry over unchanged, since only the sink implementation changes.
- The identifiers-only `Data` rule (decision 5) is expected to hold after the immudb sink
  ships too — it is not specific to the log-only PoC, but recorded here because this is where
  the audit `Data` shape was first decided.
