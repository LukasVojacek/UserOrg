# ADR 0116 - An unknown sealing key is retry or dead-letter by cause, and the producer's key cache follows the proxy's store

- Date: 2026-10-09
- Status: accepted
- Supersedes / amends: **ADR-0096 §2** - the list of permanent rejections that dead-letter gains a
  precise meaning for "unknown key" (422 `sealing.unknown_key`), and a 503 for a store that is not
  loaded is a retry; and **ADR-0097's warm-cache consequence** - a warm producer cache is no longer a way
  to seal to a key nothing can open, so per-run fixture directories are no longer what keeps that
  correct. ADR-0096 §1, §3-§7 and ADR-0097 §1-§5 are unchanged.
- Relates to: [ADR-0096](0096-audit-log-ships-sealed-to-immudb-through-an-unsealing-proxy.md),
  [ADR-0097](0097-audit-proxy-key-ring-gains-a-file-mode-and-drops-its-vault-token.md),
  #332, #351, proxy !8, ogham#8, Lukdrasil.StepUpLogging 5.2.0

## Context

A producer caches the proxy's public sealing-key document on disk and, until now, a warm cache satisfied
`RequireKeyAtStartup` without asking the proxy. When the proxy's store was recreated, the producer kept
sealing to the old key. The proxy answered `UnknownKey`, the drain treated that as transient, and the spool
stalled forever with nothing to read it back - ADR-0097 named the state and worked around it in the
fixtures and in the dev topology, and left the deployment case open until the producer side of #332.

The producer side is Ogham.Sealing 0.3.0 (ogham#8), the proxy at 0.2.0 (!8) and StepUpLogging 5.2.0; this
repository pins those versions.

## Decision

1. **Unknown key is split by cause.** The proxy answers 503 when its sealing key store is not loaded yet -
   the key may still appear, so the drain retries. It answers 422 `sealing.unknown_key` when the store is
   loaded and the kid is not in it - the key is gone, the envelope can never be opened, and the drain
   dead-letters it with reason `rejected`.
2. **FIFO is kept only for transient errors.** A transient failure still blocks the envelopes behind it in
   order; a permanent rejection moves the one envelope aside and the spool goes on.
3. **The producer asks the proxy first.** At startup Ogham.Sealing 0.3.0 fetches the key document live
   before trusting the cache. The document carries a `storeEpoch`; a changed epoch discards the cache, so
   the first record after a proxy store change is sealed to the current key.
4. **Retired keys stay listed in `accepted` for the proxy's retention window.** The proxy never deletes a
   key file; `KeyRetention__RetiredKeys` (default 30 days) only decides which retired kids `accepted` still
   names, and a key past the retention still opens an envelope sealed to it. The overlap rule stays the one
   ADR-0096 states for password rotation: the overlap is longer than the oldest record in any spool.
   `OldestRecordMaxAge` (15 minutes here) is not that rule; it is the spool health threshold of decision 5.
5. **Dead-letter makes the spool health Unhealthy, and the oldest record is exported** as
   `audit_spool_oldest_age_seconds`, so a stall is visible before and after it becomes a loss.

## Consequences

- A running producer is exposed to a replaced store until its next successful fetch: at most `RefreshInterval` plus the proxy's
  `KeyDocumentCacheDuration`. Envelopes sealed in that window dead-letter instead of stalling. A restart is covered by decision 3.
  `AddServiceDefaults` sets `RefreshInterval` to 1 minute (the package default is longer), so the window is 1 minute
  plus `KeyDocumentCacheDuration`; `OghamSealing:RefreshInterval` in configuration overrides it. The key refresh health
  check turns Degraded after 3 missed intervals.
- A legacy 0.0.1 store has no epoch and mints one once. A producer discards its cache for an epoch change only
  when both the cache and the fetched document carry an epoch, so a cache written before epochs existed
  (Ogham.Sealing 0.2.1) is not discarded on that count; a fetched document with a sealable key replaces the
  cache regardless.
- The per-run, deleted-on-teardown audit directories of ADR-0097 are no longer what keeps the E2E topology
  correct; `AuditWarmCacheTests` proves that a producer whose cache comes from an earlier proxy store seals
  with the current key and drains.
- The `file-ring` directory segment of the dev topology stays: it separates layouts of the Vault-ring era
  from the file ring's, and is not the warm-cache guard.
