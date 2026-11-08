# An internal precondition failure fast-fails as 500, distinct from HEAppE total degradation (503)

## Context

ADR 0073 established that a TemplatesInfo build does a single attempt and that
**total degradation** — every queried HEAppE instance failed — returns **503**
with title `"HEAppE service unavailable"`. That contract is correct for failures
that originate in the remote HEAppE instances (unreachable, unauthorized, timeout,
upstream 5xx, hydration skeleton).

Two problems surfaced once we looked at *why* a build was failing:

1. **The 503 discarded the cause.** A failed per-instance query was collapsed in
   `ProjectService.ProcessUrlGroupAsync` to `(Responses, bool Degraded, string? UnavailableInstance)`
   — the underlying `Error` was thrown away. At the point `HeappeUnavailableException`
   was raised there was no information left about whether the cause was a rejected
   token (`401`), a timeout, an unreachable host, or something else. An operator had
   to correlate the request trace id against server logs to recover it.

2. **A non-HEAppE failure masqueraded as HEAppE being down.** Building a project's
   templates has a precondition: the set of **active command templates** for the
   project, read from the Heappe integration module's own database
   (`HeappeIntegrationDbContext`, populated by sync — *not* the remote HEAppE
   instance). That read happened *inside* the per-instance adapter call
   (`HeappeConnectionService.GetListAvailableClusters`), once per link group. When
   that database read failed, the group was marked degraded exactly like a remote
   HEAppE failure. With the DB down, every group failed the same way, so the build
   reported **total degradation → 503 "HEAppE service unavailable"** — blaming the
   remote HPC system for an outage on our own side.

These are two facets of one gap: the response did not distinguish *whose* fault the
failure was, and an internal precondition failure was indistinguishable from a remote
outage.

## Decision

**Classify the failure cause at its source, carry only a sanitized cause up, and split
the response by who is at fault.**

1. **Causes are stable, URL-free `Error` codes set where the failure is known.**
   Each failure site stamps an `Error.Code` (the `ErrorBuilder.WithCode` API):
   `unauthorized` (HTTP 401/403), `upstream-error` (other non-success), `timeout`,
   `invalid-response` (deserialize / hydration skeleton), `unreachable` (liveness
   gate), `misconfigured` (active link's cluster absent from the catalog). Any
   unrecognized code maps to a defensive `internal` fallback. The code carries no URL
   and no response body, so it is safe to surface.

2. **`ProcessUrlGroupAsync` propagates `(Descriptor, Cause)`, never the raw `Error`.**
   The descriptor is the existing sanitized `provider / resource (locations)` string
   (no HEAppE URL). The raw `Error` — whose `Description` *does* contain the URL and
   response body — is mapped to a cause at the group boundary and not carried further,
   so it cannot later leak into a response.

3. **HEAppE total degradation stays 503, now with a cause-bearing body.** When every
   instance fails for a HEAppE-side reason, `HeappeUnavailableException` carries the
   per-instance `failures` list and `GlobalExceptionHandler` renders it into
   `ProblemDetails.Extensions["failures"] = [{ instance, cause }]`. Status and title
   are unchanged.

4. **The command-template precondition is hoisted to once-per-build and fast-fails as
   500.** `BuildProjectTemplateInfoAsync` reads the project's active command templates
   **once**, before the per-instance `Task.WhenAll`, and passes the set down into each
   `GetListAvailableClusters` call (which no longer reads it itself). If that read
   fails, the build throws an internal-error exception that `GlobalExceptionHandler`
   maps to **500** — before any HEAppE instance is queried. An internal precondition
   failure is therefore never reported as HEAppE degradation, and the duplicate
   per-group DB read is removed.

## Consequences

- **The two failure classes are now distinguishable from the HTTP response alone.**
  `503` + `failures[].cause` means "a remote HEAppE reason — here is which one, per
  instance"; `500` means "our precondition (the command-template DB) failed, retrying
  will not route around it." Neither requires reading server logs to triage.
- **The HEAppE URL still never leaves the service.** Only the sanitized descriptor and
  the cause code reach the body; the raw `Error.Description` is dropped at the group
  boundary.
- **ADR 0073's "total degradation → 503" is refined, not reversed.** It now reads:
  total degradation *from HEAppE-side causes* → 503; failure of an internal precondition
  → 500 fast-fail. Partial degradation is unchanged (200 + `unavailable-instances`
  header), and a degraded build is still never cached.
- **Fail-fast ordering changed.** The command-template read now happens before the
  HEAppE fan-out, so a DB outage returns 500 in roughly the DB timeout rather than after
  the per-instance liveness gates. A project with no active HEAppE links skips the read
  entirely.
- **`internal` is a defensive fallback.** With the hoist, a command-template DB failure
  surfaces as 500 directly and does not appear in `failures[]`. Within the HEAppE fan-out,
  `internal` covers our-side failures (a null response or an unexpected exception) and any
  unrecognized/missing `Error.Code` — never a recognized HEAppE cause.
- A new caller branching on status now has a meaningful 500-vs-503 distinction to honor;
  callers that previously treated every TemplatesInfo failure as "HEAppE down" will see
  internal failures move from 503 to 500.
