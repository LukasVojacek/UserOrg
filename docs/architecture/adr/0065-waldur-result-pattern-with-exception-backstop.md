# Result pattern for expected failures, exception backstop for the rest

**Status:** accepted (grilling session 2026-07-17) · amends ADR-0057 · relates to ADR-0056, ADR-0062

## Context

The original goal: the fetch-and-process cycle must not throw exceptions that abort the whole
iteration; expected failures should be caught at the nearest boundary, turned into a logged
result, and the cycle should carry on. Two layers throw today:

- **Fetch layer** — the API client throws `WaldurApiException` on any non-success/network/
  pagination failure. Membership already converts this to a hand-rolled `MembershipSnapshot.Failed()`.
- **Consumer layer** — event handlers throw (`RequireLocationByEndpoint`, `RequireProject`,
  `RequireCountry`, …) when a dependency is missing. The throw is the integrity mechanism: it
  aborts the handler transaction before `SaveChanges`, so nothing is acked and the event replays
  (ADR-0057).

The project's established idiom (CLAUDE.md) is `Lukdrasil.Result<T>` / `Error` for **expected**
failures, throwing **only** for programming errors.

## Decision

Adopt the Result idiom end-to-end for expected failures, with an exception backstop:

- **Fetch boundary:** every per-item fetch returns `Lukdrasil.Result<T>`; on failure the caller
  produces the appropriate carry-forward (ADR-0062) or skip (ADR-0063). The existing
  `MembershipSnapshot.Failed()` is **harmonized** to the same Result idiom — one idiom everywhere.
  *(`Result<T>` is the fetch outcome; the diff still needs the separate "incomplete snapshot /
  null-payload" concept for carry-forward — the two are not conflated.)*
- **Consumer boundary:** handler dependency guards return `Result.Failure` **before** `SaveChanges`
  (nothing commits) instead of throwing. `IWaldurEventHandler`/`PublishAsync` return `Result`. A
  failure Result → no SHA written → replay next iteration: the ADR-0057 integrity/replay semantics
  are preserved, now driven by a value instead of an exception.
- **Backstop:** the per-event `try/catch` in `ApplyActionsAsync` **stays** as defense in depth for
  **unexpected** exceptions — infrastructure faults (`SqlException`) and programming errors
  (`ArgumentNullException`) still throw and are caught here, logged, not acked. Only expected
  dependency-missing failures become Results; unexpected failures keep the exception path.

## Consequences

- No expected-failure exception crosses an iteration; each is caught at its source and logged.
- Replay/integrity behavior is unchanged in effect — a failure (Result or backstopped exception)
  still writes no SHA and retries next interval.
- The publisher and every event handler change signature to return `Result`; the per-event loop
  distinguishes a `Result.Failure` (structured expected error) from a backstopped exception
  (unexpected) in its logging, but both are no-ack-and-continue.
