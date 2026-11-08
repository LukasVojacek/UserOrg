# Per-iteration TraceId, per-entity SpanId, log-only correlation

**Status:** accepted (grilling session 2026-07-17) · relates to ADR-0058, ADR-0062

## Context

When a fetch fails and the cycle continues (ADR-0062/0063/0064), an operator must be able to take
a remote id (Waldur uuid) from a log line and reconstruct the whole processing of that entity —
what was skipped and why. Today the worker opens one Activity per iteration
(`RunIterationAsync`) and threads only an `{Instance}` property into logs; there is no per-entity
correlation and no trace/span id in the log lines. The `Waldur.Sync` `ActivitySource` already
exists and is registered with the OpenTelemetry tracer provider.

A W3C **TraceId is constant across a whole trace tree** — child spans share it and differ only by
**SpanId**. So "a unique id per dependency group" is expressed as a unique **SpanId** within the
iteration's trace, not a new TraceId per group.

## Decision

- The iteration Activity stays the **trace root** → one **TraceId per iteration**.
- Each inner unit opens a **child span** → a unique **SpanId per project, per resource, per user**.
  Project/user spans open around the publish work in `ApplyActionsAsync`; resource spans open in
  the consumer resource loop; fetch-phase per-item failures are wrapped in a span/scope too so
  their logs carry the ids.
- Every log line is stamped, via `ILogger.BeginScope`, with **TraceId + SpanId + remote id**
  (`ExternalId`). An operator greps the remote id → gets TraceId (the whole iteration) and SpanId
  (the specific entity subtree).
- Correlation is **log-only**. The OTLP exporter is **not** wired in ServiceDefaults and wiring it
  is **out of scope** (it is shared infrastructure affecting every service). TraceId/SpanId are
  still valid — with the source registered and the default `ParentBased(AlwaysOn)` sampler,
  `StartActivity` returns a real Activity even with no exporter, so `BeginScope` stamping works.

## Logging volume (amends ADR-0058 for this path)

- A root fetch/dependency failure is logged at **Error** with `ExternalId` + reason + TraceId/SpanId,
  via a dedicated marker. Repeated Error lines for a persistently-failing item every iteration are
  accepted — no dedup/throttle (YAGNI).
- Each proactively **skipped dependent** is logged at **Debug** (suppressed in production, enabled
  for troubleshooting), keeping normal-operation volume minimal.

## Consequences

- Failure investigation is greppable by remote id today, without depending on a trace backend.
- If the OTLP exporter is later wired, the same spans light up as a trace tree with zero rework.
- Cost is one Activity per entity plus a `BeginScope` per log site; negligible at a 30-minute cadence.
