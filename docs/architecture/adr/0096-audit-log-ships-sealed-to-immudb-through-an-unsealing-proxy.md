# ADR 0096 - Audit records ship sealed to immudb through an unsealing proxy; fail-closed for P1 now means "spooled", not "archived"

- Date: 2026-08-14
- Status: accepted
- Supersedes / amends: **ADR-0088 §2** (the log-only sink) in full - `LoggingAuditSink` is deleted and
  the immudb sink it forecast has landed; and **narrows ADR-0088 §4** - what a fail-closed P1 call
  site now proves is that the record was spooled, not that it reached the archive. ADR-0088 §1
  (never SQL), §3 (not through the outbox) and §5 (identifiers only) are unchanged and still binding.
- Relates to: [ADR-0004](0004-persistence-sql-immudb.md) (immudb as the audit store),
  [ADR-0088](0088-audit-log-poc-narrows-adr-0004.md) (the PoC this executes)

## Context

ADR-0088 recorded a log-only sink as a deliberate, time-boxed interim, with immudb as the target it
would be replaced by. That replacement is what this change is. The chain is now:

```
producer (Core API, Gateway)
  └─ EncryptedSpoolAuditSink (StepUpLogging)
     └─ AuditSqlSealer - composes the parametrized immugw sqlexec INSERT + immudb credentials,
        seals the pair into one Ogham envelope
        └─ spool file on the producer's disk (sealed bytes only)
           └─ drain worker POSTs one envelope per request to the audit unsealing proxy
              └─ proxy unseals, forwards verbatim to immugw -> immudb
```

The proxy knows nothing about `audit_log`: it opens the envelope, replays the login it finds inside
and forwards the request it finds inside. Everything about the archive - the columns, the widths, the
timestamp, the login - is decided in `AuditSqlSealer`, on the producer.

## Decision

1. **The immudb sink replaces the log-only sink** (supersedes ADR-0088 §2). Audit records are sealed
   at the producer and land in immudb through the unsealing proxy. ADR-0004's immudb half is executed
   for the first time.

2. **Fail-closed for P1 now guarantees durability, not archival** (narrows ADR-0088 §4). Delivery is
   asynchronous: the sink's write is a local spool write, and the P1 request is answered on its
   outcome alone. A failure the caller sees still means "this change was not audited" - but a success
   the caller sees now means only "the record is on the producer's disk". What happens afterwards is
   invisible to that caller: a transient failure (503, unreachable proxy, immugw refusing) keeps
   retrying, while a permanent rejection (400/413/422 - bytes that can never become readable)
   dead-letters the record with the request long since answered 200. Loss after the spool write is
   therefore detectable **only out of band** - the dead-letter box and the spool health check - and
   never in the response. Anything that needs "the record is in the archive before the caller is
   answered" is not satisfied by this design and must not claim to be.

3. **The producer holds the immudb credentials**, and they travel inside the sealed envelope rather
   than being configured on the proxy. The proxy is a transport, not a trust boundary: it replays
   whatever login it unseals. A compromised producer can therefore read the audit archive as well as
   write to it - sealing protects the disk and the wire, not the host.

4. **Producers are not authenticated to the proxy; the transport is the only compensating control.**
   Ogham gives no origin authenticity - the sealing key is public, so anyone inside the network can
   produce a well-formed envelope, though only someone holding the credentials can make it write.
   Every producer-facing hop is therefore HTTPS: the `POST /audit` endpoint *and* the key document at
   `/.well-known/ogham-sealing-keys.json`. The key document is the sharp edge: an on-path attacker
   who substitutes it makes every producer seal to a key the proxy can never open - a silent audit
   blackhole rather than a visible failure. This scope covers the producer-facing hops only; inside
   the local Aspire topology the proxy still reaches immugw over plain HTTP on the container network,
   and by that hop the envelope is already open: it carries the unsealed immudb credentials on the
   replayed `POST /login` and the complete audit row - actor, target, old and new values - in the
   `sqlexec` INSERT, both in the clear. The sealed payload is never exposed; its decrypted content on
   that last hop is. "The whole path over HTTPS" is therefore narrowed to **every producer-facing
   hop**, and what would close the remainder is TLS on immugw, or co-locating the proxy with immugw so
   the hop never leaves the host. Adding a producer token later closes the authentication half without
   changing anything above.

5. **The unsealing proxy is a separate deployable with zero references to any UserOrg project**, so it
   can be lifted into its own repository by moving its folder. Four things it depends on live outside
   that folder and have to be recreated on the other side: the `Ogham.Sealing` version pin in
   `Directory.Packages.props` (central package management), the `opencode-project-962` source and its
   `Ogham` / `Ogham.*` mapping in `nuget.config`, the root `Directory.Build.props`, and the proxy's
   test project (moved out with the proxy, ADR-0098). Its Data Protection key ring lives
   entirely in Vault: it wraps the private half of every sealing key ever issued, and losing the ring
   means losing the ability to open every envelope still in flight.

6. **`Ogham.Sealing` comes from a second NuGet feed**, mapped to `Ogham*` package patterns only, with
   everything else left on nuget.org. A second feed is a second root of trust; the mapping is what
   keeps it from being one for the whole dependency graph.

7. **Duplicate rows are accepted.** Delivery is at-least-once (`noWait`, retried deliveries), and an
   append-only archive cannot dedupe on write. `eventId` travels in the `metadata` document as the
   deduplication key for whoever reads the archive. A duplicate is noise; a lost record is a lie.

## Consequences

- ADR-0088 stays as the record of the interim state and of the decisions this ADR does not touch;
  its §2 is history and its §4 must be read through decision 2 above.
- Rotating the immudb password invalidates whatever is already sealed in a spool: the envelope
  carries the credentials that were current when it was written. The new password has to be added
  before the old one is removed, with an overlap longer than the oldest record in any spool.
- Audit configuration is mandatory: a host that cannot seal refuses to start rather than serve
  traffic it cannot audit. The one exception is the OpenAPI document-generation host, which serves
  nothing.
- The sealed wire shape is modelled independently on both sides (producer and proxy) and is not
  versioned. One contract test crosses that boundary
  (`SealedWireContractTests`, which moved with the proxy's tests, ADR-0098); when the proxy leaves this
  repository, that test is what has to be re-pointed rather than deleted.

## Note (2026-09-29): DrainWorker is the only retrying layer on the producer-to-proxy edge

Decision 2's "keeps retrying" is DrainWorker's backoff and nothing else. A Polly retry under
DrainWorker added duplicate rows (decision 7) and logged its last attempt at Error, which stepped
logging up. Amended 2026-09-30 (Lukdrasil.StepUpLogging 5.0.2): `AddEncryptedSpoolAuditSink` itself
strips every resilience handler registered on the spool's HttpClient (`Lukdrasil.StepUpLogging.Audit`)
before it, the standard handler inherited from `ConfigureHttpClientDefaults` included, so userorg
registers no pipeline on that client. `EncryptedSpool:DeliveryTimeout` is the only attempt timeout;
`src/UserOrg.ServiceDefaults/Extensions.cs` sets it to 10 s before binding the `EncryptedSpool`
section, which can still override it. DrainWorker logs a timed-out attempt at Warning, so a hung proxy
never triggers auto step-up (ADR 0112 QS-07). As read from
Lukdrasil.StepUpLogging 5.0.2, DrainWorker doubles its wait from `DrainInterval` (5 s) to
`MaxDrainBackoff` (5 min), with no documented jitter. An unreachable proxy reports the spool check
Degraded, set in code, so a producer stays in readiness while P1 writes still spool. The operator view
is `docs/runbooks/audit-chain-local-recovery.md`.

## Note (2026-10-08): the drain worker posts with a Content-Length

As read from Lukdrasil.StepUpLogging 5.1.0, DrainWorker posts the stored bytes of a spooled record with
a `Content-Length` header instead of chunked transfer encoding, so the unsealing proxy receives a sized
body. `DeliveryBatchSize` stays at its default of 1: the proxy exposes only `POST /audit`, not
`/audit/batch`, so each record is still posted alone.
