# Command template sync reports per-location partial results instead of an all-or-nothing bool

## Context

`ProjectCommandTemplateSyncService.SyncCommandTemplatesForResource` already groups a
project resource's `LocationAssignment`s by distinct `HeappeInstanceSyncEntry` (see the
prior investigation confirming per-assignment `HEAppEUrl` is resolved and grouped
correctly) and already **continues processing** when one instance or one location
aggregation fails — the `foreach` loops accumulate failures into a single
`ValidationError errors` collection and `continue` rather than abort.

So partial *processing* already happens: if location A's cluster is missing but
location B's HEAppE instance is healthy, B's command templates still get created or
updated in the DB.

The gap is in the **result contract**, not the processing loop:

- The method returns `Task<ValidationResult<bool>>`. On any accumulated error it
  returns `ValidationResult<bool>.Failure(errors)` — a flat list of `Error` values with
  no indication of *which* location/instance each error belongs to, and no record of
  which locations succeeded at all.
- The endpoint (`ProjectCommandTemplatesSyncEndpoint`) collapses this further: any
  failure at all becomes `400 BadRequest { success = false, error }`, even when most of
  the resource's locations synced correctly.

A caller (a support engineer, or an automated retry) cannot tell "3 of 4 locations
synced, VLQ's HEAppE instance was unreachable" from "nothing synced." They have to
correlate server logs by project/resource name to find out what actually happened,
mirroring the problem ADR 0074 solved for TemplatesInfo — but here for a write/sync
operation rather than a read.

**Usage reality check.** `IProjectCommandTemplateSyncService.SyncCommandTemplatesForResource`
has exactly two call sites in the whole codebase:

1. `POST /CommandTemplate/Synchronization` (`ProjectCommandTemplatesSyncEndpoint.cs:33-35`)
   — no in-repo caller today, but it remains the deliberate **manual re-sync entry
   point** for when something changes directly on the HEAppE side (cluster
   reconfigured, command template edited out-of-band) and an operator needs to force a
   resync without waiting for the next periodic pull. Gateway-routed, RBAC-gated by
   `perm:heappe:manager` (`roleManagmentRBAC.json:1162-1171`). Kept in scope.
2. `WorkItemProcessor.ProcessAsync` (`WorkItemProcessor.cs:85`) — the actual production
   path, fed by three producers into the same `Channel<WorkItem>`: manual project-resource
   assign/unassign (`ProjectEndpoint.cs:190,218`), `ProjectResourceAssignmentService`
   insert/update/delete (`ProjectResourceAssignmentService.cs:94,134,190`), and a
   periodic full Waldur pull every `SyncIterationDelayMinutes` (default 30 min,
   `WaldurExportOrchestrator.cs:154-155` via `ProcessingBackgroundService`). Today this
   path calls `await _syncService.SyncCommandTemplatesForResource(...)` **without
   capturing the result at all** — a partial failure here (the far more common trigger
   path than the endpoint) is invisible even in logs beyond "sync completed", with no
   breakdown of what failed.

So the reporting gap is more severe on the background path than the endpoint: the
endpoint at least returns *something* to a caller (when it has one); `WorkItemProcessor`
silently discards the outcome on the path that fires every 30 minutes and on every
manual assignment change.

## Decision

**Track a success/failure outcome per location aggregation (location + queue), and let
the endpoint return `200 OK` with the full breakdown whenever at least one location
succeeded.**

1. **New result shape**, in `Heappe.Contracts` (mirrors the existing sync entry
   records):
   ```csharp
   public sealed record LocationSyncResultEntry(
       Guid LocationId,
       string LocationName,
       string? AggregationName,
       Uri HeappeInstanceUri,
       bool IsSuccess,
       IReadOnlyList<Error> Errors);

   public sealed record CommandTemplateSyncResult(
       IReadOnlyList<LocationSyncResultEntry> Locations);
   ```
   `Locations` has one entry per `LocationAggregationEntry` that was actually attempted
   (i.e., one per distinct location+aggregation pair under `BuildLocationGroups`).

2. **`SyncCommandTemplatesForResource` returns `Task<ValidationResult<CommandTemplateSyncResult>>`.**
   Failures that happen *before* any location is known (project/resource lookup fails,
   allocation expired, resource has zero HEAppE locations) are unattributable to a
   specific location and keep today's behavior: an early `ValidationResult<...>.Failure(...)`,
   no `CommandTemplateSyncResult` produced.

3. **Instance-level failures are broadcast to every location under that instance.**
   When `GetHeappeConnectionInfoAsync`, `GetProjectByResourceName`,
   `GetProjectCommandTemplatesAsync`, or `GetClusters` fails for a given
   `HeappeInstanceSyncEntry`, every `LocationAggregationEntry` grouped under that
   instance gets a `LocationSyncResultEntry` with `IsSuccess = false` and the same
   `Error`, instead of only appending to a flat error list. This preserves the existing
   `continue`-to-next-instance behavior — it only changes what gets recorded.

4. **Location-level failures (missing cluster, missing aggregation, no command template
   definitions, individual `CreateCommandTemplateAsync` failures) mark only that one
   location's entry as failed**, with all `Error`s encountered for that location
   collected into its `Errors` list. If a location aggregation has zero errors after
   processing all its command template definitions, it gets `IsSuccess = true`.

5. **Location-partition sync failures** (`ProcessLocationPartitionSynchronizationAsync`)
   are location-scoped already (`LocationSyncEntry` per distinct location) — the same
   location's `LocationSyncResultEntry` picks up that error too and is excluded from
   further per-template processing for that location (a location whose partitions
   failed to sync cannot correctly sync templates against stale partition data).

6. **Endpoint contract**: `200 OK` whenever `Locations` is non-empty and at least one
   entry has `IsSuccess = true` — body is the `CommandTemplateSyncResult`. `400
   BadRequest` when every attempted location failed, or when the top-level
   `ValidationResult` itself failed (project/resource/allocation-level). No `207
   Multi-Status` — the API surface stays 2xx/4xx, consistent with the rest of this
   module, and callers only need to branch on status once (`2xx` → look at the body's
   per-location list for detail) rather than learn a new status code.

7. **`WorkItemProcessor` gains structured per-location logging (no response contract,
   since it has no HTTP caller).** `ExecuteSyncWithTelemetry` (`WorkItemProcessor.cs:61-112`)
   captures the `CommandTemplateSyncResult` from the now-returned
   `ValidationResult<CommandTemplateSyncResult>` and:
   - Logs one structured summary line: counts of succeeded/failed locations, and for
     each failed `LocationSyncResultEntry`, its `LocationName`/`AggregationName`/
     `HeappeInstanceUri` and `Errors`. Use the existing `ILogger` (structured logging
     already used throughout this class), not a new sink.
   - Sets `syncActivity`/`processActivity` tags for success/failure counts (the class
     already tags `TagDurationSeconds`/`TagSuccess` on the OpenTelemetry activities at
     lines 91-94; extend with e.g. `TagLocationsSucceeded`/`TagLocationsFailed` so this
     is visible in traces, not just logs).
   - `TagSuccess` becomes "all locations succeeded" (mirrors `AllSucceeded`) rather than
     "the call didn't throw" — today it's set to `true` unconditionally in the non-throw
     path, which is misleading once partial failures are trackable.
   - Does **not** change `ProcessAsync`'s control flow (dedup/timeout/retry logic is
     unaffected) and does **not** propagate partial failure as an exception — a partial
     result is not a processing failure, it is a fully-processed outcome with some
     locations unsynced, consistent with point 3-4 above.

## Consequences

- **A caller can now distinguish "fully synced," "partially synced (here's what and
  why)," and "nothing synced"** without reading server logs — same principle ADR 0074
  applied to TemplatesInfo reads, now applied to this write path.
- **The processing loop's behavior does not change** — it already continued past
  per-instance and per-location failures. This is purely a richer, more honest
  reporting of outcomes that were already happening silently.
- **Breaking response-contract change.** Any existing client of `POST
  /CommandTemplate/Synchronization` that only checked for `200` with body `true` must
  be updated to read `CommandTemplateSyncResult.Locations` instead. This is acceptable
  because the only known caller today is manual/support tooling.
- **`Error` objects (not plain strings) are preserved per location**, keeping the
  existing severity/description/code structure so downstream consumers do not lose
  fidelity compared to today's flat `ValidationError`.
- Locations whose partition sync failed are intentionally **not** attempted for
  template sync — reporting them as `IsSuccess = false` with the partition error is more
  useful than a misleading template-level error against partitions that are known stale.

### Relationship to ADR 0074 (HEAppE URL non-leakage) — deliberate divergence

ADR 0074 established that the **TemplatesInfo read path** must keep the HEAppE URL and
raw `Error.Description` (which can embed the URL and upstream response detail) OUT of its
HTTP responses, sanitizing them to URL-free cause codes + descriptors, because that
endpoint is broadly reachable.

This sync endpoint **deliberately diverges**: `LocationSyncResultEntry.HeappeInstanceUri`
and the raw `Error` list (whose descriptions name the HEAppE instance) ARE returned in
the HTTP body. This is an intentional, bounded choice, not an oversight:

- `POST /api/Heappe/CommandTemplate/Synchronization` is a **manager-gated write path**,
  routed behind `perm:heappe:manager` (`roleManagmentRBAC.json`). Only a HEAppE manager
  can call it, and a manager operating a manual re-sync legitimately needs to see *which
  cluster* failed and *why* — a sanitized cause code would strip exactly the diagnostic
  they came for.
- The blast radius is therefore limited to principals already trusted with HEAppE
  administration, unlike the broader TemplatesInfo read.

So ADR 0074's non-leakage rule stands for the read path; this write path opts out
consciously because its audience is the administrator who needs the raw detail. If this
endpoint's authorization is ever broadened beyond `perm:heappe:manager`, this decision
must be revisited and the response sanitized (map `HeappeInstanceUri`/`Errors` to a
URL-free shape for the body, keeping the raw form only in server-side
`WorkItemProcessor` logs).
- **The background path (the actual production trigger, firing on every assignment
  change and every 30-minute pull) now surfaces partial-sync detail in logs/telemetry**
  instead of a silent "sync completed" — this closes the more impactful half of the
  original gap, since the endpoint alone is a manual/low-volume path.
- `WorkItemProcessor`'s log volume increases modestly (one structured line per
  processed work item listing failed locations, where previously nothing beyond
  start/complete was logged) — acceptable since it's proportional to actual failures,
  not per-location on the happy path.
