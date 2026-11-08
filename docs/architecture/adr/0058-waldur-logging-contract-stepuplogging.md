# Waldur sync logging contract (StepUpLogging levels)

**Status:** accepted (grilling session 2026-07-06) · relates to ADR-0053, ADR-0056

## Context

A stated goal of the refactor is clean logs with precise per-item failure attribution: it must be
obvious *which* item failed, *in which phase*, and *why*. Today two `LogMessages.cs` files define
488 lines of messages across the channel pipeline, including per-item logging of unchanged items.

The platform uses **StepUpLogging** (in-house Serilog extension): Warning/Error always log, and
their occurrence temporarily "steps up" the sink so Information-level context around the failure
surfaces too. It also offers an always-log bypass for selected Information messages.

## Decision

Levels are the contract now; concrete wiring through StepUpLogging is an implementation-phase
concern.

| Level | Used for |
|---|---|
| **Debug** | Detail needed only when troubleshooting: payload summaries, SHA old/new comparison, per-event emission (`instance, eventType, externalId`). |
| **Information** | Start and end of processing of each Waldur entity (`instance, entityType, externalId`). Normally suppressed; surfaces via step-up around a failure, giving exactly the context of the failed item. |
| **Information (always-log bypass)** | Iteration summary, 2 lines per instance per iteration: start (`instance, trigger`) and result (`duration, fetched, created, updated, removed, unchanged, failed`). |
| **Warning / Error** | One structured entry per failed item: `instance, entityType, externalId, phase (fetch\|diff\|publish\|handler\|heappe), reason, outcome (retry next iteration)`. A whole-phase failure (e.g. Waldur API down) logs **once** per iteration, not once per item. |

**Unchanged items produce no log line at any level.** With no changes and no failures, an
iteration costs exactly two always-log lines.

Metrics (OpenTelemetry) shrink to iteration-summary counters/histogram (result counts, duration,
SHA-map size). Channel-depth gauges and worker-pool metrics die with the pipeline (ADR-0056).

## Consequences

- ~15 structured message definitions replace the current 488 lines.
- A permanently failing item is visible as a repeating error with a stable `externalId` — the
  monitoring hook that replaces the dead-letter queue.
- Log volume no longer scales with data volume, only with change volume and failures.

## Amendments (independent review 2026-07-06)

### A1 — Absence warns on transition only

D3 ("absence = warning") must not mean a warning **every iteration**: a project the instance
legitimately no longer serves would then step up the StepUpLogging sink every 30 minutes forever,
destroying the step-up signal. Rule: a Warning is emitted only on the **transition into absence**
(item was in the SHA map, is not in the current successful pull — trackable in the map); ongoing
absences appear as a count in the always-log iteration summary
(`…, absent: N`). Same rule for the reverse transition (item reappears → Information).

### A2 — Cascade error class

When a parent item fails permanently (e.g. one provider), its dependent items fail every
iteration by design (ADR-0056/A1). These child errors keep their per-item structured Error
entries (they carry the attribution), but the iteration summary must aggregate them
(`failed: N (M blocked-by-parent)`), so a single root cause does not read as N independent
problems in monitoring.

