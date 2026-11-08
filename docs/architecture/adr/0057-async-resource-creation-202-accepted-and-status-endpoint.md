# Async resource creation returns 202 Accepted with a status poll endpoint

## Context

`CreateResourceOnLocation` was rewritten (MR !276) to submit an async workflow instead of creating
the resource synchronously, but it kept the old contract: it returned `201 CreatedAtRoute("GetResourceByNameV2", …)`
and branched on `if (response.Status == RequestStatus.Failed)`. Both are wrong for an async submit:

- `IRequestApplicationService.SubmitAsync` "returns immediately after the request is persisted and
  enqueued" and yields `RequestStatus.Accepted` — it **never** returns `Failed` synchronously
  (`RequestStatus` xml-doc). So the `Failed` branch is dead code.
- The `201` points at `GetResourceByNameV2` for a `ProjectResource` that only gets created **later**,
  inside `InitializeProjectResourceHandler`. Following the `Location` immediately 404s — the contract lies.
- The endpoint also still calls `unitOfWork.SaveChangesAsync(ct)`, but nothing is added to the unit of
  work in the handler anymore (persistence moved into the workflow steps) — a no-op that misleads.
- There is **no** HTTP endpoint anywhere exposing workflow/request status by id; `GetStatusAsync` is
  only consumed by the Sample and unit tests. A 202 `Location` has nothing to point at today.

## Decision

1. **Return `202 Accepted`.** The endpoint returns `202` with a body carrying the `RequestId`
   (the flow id), and a `Location` header pointing at a new status endpoint (below). Drop the dead
   `Failed` check and the no-op `SaveChangesAsync`. Update `Produces`/`MapDocumentation` to `202`.
2. **Add a status GET endpoint.** New Minimal API endpoint `GET api/v2/ResourceRequests/{requestId}`
   backed by `IRequestApplicationService.GetStatusAsync(requestId)`, projecting `StatusResponse` to a
   response DTO (flow status + per-step summary), `404` when the id is unknown. It follows the repo's
   endpoint conventions (`GetResourceRequestStatusEndpoint`, `HandleAsync`, `Request`/`Response`
   records, `MapDocumentation`) and is mapped in `EndpointsExtensions.MapCoreEndpoints`.
3. **`Location` targets the status route** via its `WithName`, so the 202 is a well-formed async
   accepted contract: caller submits, gets `202` + poll URL, polls until Completed/Failed.
   `SendEmailStep` is the terminal step of the create flow — `CreateStrategy.Transition` returns
   `null` after it, which is how `RequestWorker` recognises the flow is finished and marks the
   request `Completed`; the earlier catch-all throw failed every otherwise successful request on its
   last step with `PERMANENT_FAILURE "Unrecognized step type 'SendEmailStep' in CreateStrategy."`.

## Consequences

- The contract is honest: `202` means "accepted, not yet done", and the `Location` resolves to a live
  status resource instead of a not-yet-existent one.
- One new read endpoint is added to Core. It is authorization-scoped like its siblings (the caller
  must be allowed to see the request); status carries no resource data beyond flow/step state.
- Clients that previously expected `201` + resource must migrate to poll the status URL. This is a v2
  endpoint still in the refactor branch (not yet released), so no external break.
- The status endpoint is intentionally minimal (status + step names/states); richer projections can be
  added when a consumer needs them (YAGNI).
- T-005-01 (plan decisions D1/D3): `CheckHeappeHandler`, the flow's first step, is a pure pre-flight
  gate — it checks HEAppE instance liveness and allocation-unit-type existence and touches no
  persistence. It used to also look up the not-yet-created `ProjectResource` row by natural key and
  fail every brand-new resource with `RESOURCE_NOT_FOUND`, because that row is first written one step
  later by `InitializeProjectResourceHandler`. `EProjectResourceState.CheckHeappeStep` stays in the
  enum for wire/contract compatibility (`API_v2-user.json`, ADR-0020 §4) but is never produced or
  persisted — no step in the create flow sets a `ProjectResource` to that state anymore.

> **Amendment 2026-09-08 ([`0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md)).**
> The pre-flight gate described above (`CheckHeappeHandler`, the flow's first step) originally received the
> resolved `Provider` aggregate and a `List<(Location, LocationAssignment)>` in its step payload. It now
> receives `ProviderName` and `IReadOnlyList<ResolvedLocationRef>`, where `ResolvedLocationRef` is a
> `(LocationName, Endpoint)` record — the two values the liveness probe actually needs. The endpoint
> resolves the locations and projects them into refs before submitting; the handler probes the endpoints
> from the refs and derives allocation unit types from `request.Data.AssignedLocations`, **not** by looking
> a ref back up (two allocations on the same location produce two refs that are equal by value, so the
> lookup would resolve both to the first assignment and stop checking the second unit type).
>
> This is not a cosmetic change to a 202 contract: the old payload could not be serialised when the
> provider already owned an allocation, and could not be deserialised at all, so a `202 Accepted` was
> issued for a request whose first step could not be persisted or replayed. The honest `202` this ADR
> decided on depends on the payload actually surviving persistence.
