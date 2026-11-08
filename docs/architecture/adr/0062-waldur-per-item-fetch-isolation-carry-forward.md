# Per-item fetch isolation via carry-forward, replacing whole-iteration abort

**Status:** accepted (grilling session 2026-07-17) · amends ADR-0056 · relates to ADR-0053, ADR-0054, ADR-0055

## Context

`Waldur.Sync` fetches customers, per-customer offerings, projects, per-project resources and
per-project users each iteration, then diffs against the SHA breadcrumb map (ADR-0055) and
publishes events. The coarse fetch phases fan out with `BoundedFanOut` (`Parallel.ForAsync`),
whose fail-fast semantics mean a **single** failed page throws and aborts the **entire**
iteration's fetch. ADR-0056 accepted this as loud-fail-over-partial-data: "a partial list is
never presented as a complete result", because a missing item could be read by the diff as an
absence.

The cost: one unreachable customer endpoint discards a whole iteration's progress for every
other healthy customer, every interval, until it recovers.

Re-examining the diff engine changes the calculus: **there are no provider or project delete
events.** The only removal event is `UserRemoved`; project soft-delete is `IsRemoved` on an
Update; providers have no removal event. The concrete harm of a "missing" provider/project is
therefore **not** a spurious delete but breadcrumb churn: `DetectAbsences` drops the breadcrumb
and the item re-emits as a spurious `Created` next iteration.

The diff input already models the safe alternative: `ProviderDiffInput`/`ProjectDiffInput` with
`Payload: null` = *present this iteration, emit nothing, keep the breadcrumb* (today used for
ineligible-by-mapping). Membership already isolates per item via `MembershipSnapshot.Failed()`.

## Decision

Isolate detail-fetch failures per item instead of aborting the iteration:

- The per-customer offering fetch and per-project resource fetch map lambdas wrap their body in
  `try/catch`, returning a **failed result** instead of throwing (`Lukdrasil.Result<T>`, see
  ADR-0065). Cancellation is re-thrown (`catch (OperationCanceledException) when
  (ct.IsCancellationRequested) { throw; }`) so shutdown still unwinds. `BoundedFanOut` is
  unchanged — a non-throwing lambda never fast-fails its siblings.
- A failed provider/project is **carried forward**: it stays present in the diff input with
  `Payload: null`, so `DetectAbsences` never sees it as absent and its breadcrumb survives. It
  is **never omitted** from the input list.
- The host-level resilience of ADR-0056 (outer loop catch, backoff, retry) is retained as the
  backstop for the truly un-isolatable cases.

## Consequences

- One down endpoint no longer discards healthy items' progress; the cycle completes and the
  failed item is retried next interval with its breadcrumb intact.
- Carry-forward depends on knowing the failed item's id — it works for detail fetches (the item
  was listed) but not for a failed **list** page (unknown ids); that case is handled by the
  ListComplete gate (ADR-0064), not carry-forward.
- ADR-0056's "never present partial as complete" invariant is preserved *for absence detection*:
  a carried-forward item is present-with-no-payload, not absent, so no false absence is derived.
- This reverses ADR-0056's coarse-grained loud-fail only for **detail** fetches; the rationale
  holds because no provider/project delete event can be triggered by a diff.
