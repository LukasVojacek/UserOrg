# ADR 0009 - Request write path: migrate self-onboarding only, notify inline

- Date: 2026-06-03
- Status: accepted

## Decision

The v2 **Request** write path (create / accept / reject, plus the anonymous action-link handle endpoint) migrates **self-onboarding requests only**. The `POST` endpoint accepts request type `SelfOnboarding` and **rejects `ProjectResourceAssignment`** with a clear error; that flow stays on legacy until the resource-assignment write path is migrated.

Request **notifications and action links are produced inline** from the use-cases through a Core notification port (`INotificationSender`) and an action-link generator, invoked after `SaveChangesAsync` — *not* from the existing `Request` domain-event handlers. Those handlers keep their logging-only role.

**Accept / reject enforce project scope in Core**: the handler loads the request, resolves its project, and verifies the project is in the caller's scope (`projectRoles` claim) before settling it — closing a gap where the legacy routes (request id only, no project in the path) let any holder of `perm:user:request:manage` handle any request.

Emailed action links are built from a **configured public base URL** pointing at the real v2 handle route, rather than from the incoming request host and the legacy `/api/Action/Handle/` path.

## Context

- Migrating the legacy `Requests` write endpoints is the user-scope work remaining after Slice D. The `Request` aggregate (state machine via `TransitionToState`, `Delete`/`Restore`, `ActionLink`s) and the read side (`GetRequests` / `GetSentRequests` / `GetPublicRequestInfo`) already exist in the modular core; only the write endpoints and side-effects were missing.
- Legacy `AcceptAssignRequestAsync` ([ProjectRequestService.cs](../../../src/Core/Application/UserOrg/Services/ProjectRequestService.cs)) branches on `RequestType`. The `ProjectResourceAssignment` branch attaches an `AssignedProjectResourceToProject` row — i.e. it drives the **resource-assignment write path that is explicitly deferred** (Admin/ProviderAdmin-only; needs shared/non-shared modelling and HEAppE sync). The `SelfOnboarding` branch only assigns the requester as a member, which Slice D already migrated (`Project.AssignUserWithRoleAsync`).
- Action-link generation needs the request host (`HostString`) and the create-flow emails embed those links, so the orchestration is inherently endpoint-level — a post-save domain-event handler has no `HttpContext`.
- The legacy accept/reject routes carry only `{RequestId}`; the Gateway permission gate cannot scope a request-id-only route to a project, so the project-scoped check can only live in Core (same reasoning as the relative checks in ADR-0008).

## Considered options

- **Self-onboarding only (chosen).** Self-contained in user-scope land; reuses the Slice D member-assignment write path; keeps v2 free of the deferred resource dependency.
- **Migrate all request types now.** Rejected: forces migrating (or stubbing) the deferred resource-assignment path, re-opening its Admin-only scope, shared/non-shared modelling, and HEAppE sync.
- **Notifications via domain-event handlers.** Rejected for the write path: handlers run post-save without `HttpContext`, so action-link generation (host-dependent, and embedded in the create emails) could not live there — it would split one logical step across two mechanisms.
- **Replicate the legacy link URL verbatim** (request host + `/api/Action/Handle/`). Rejected: carries forward a latent path mismatch and is fragile behind proxies.

## Consequences

- v2 `POST /v2/Requests` is **not** a drop-in for legacy `POST api/Requests`: a `ProjectResourceAssignment` body that legacy accepts is rejected by v2. Clients needing that flow stay on legacy until it is migrated.
- The Core module gains an **outbound notification dependency** (email sender + action-link store) behind a port, mirroring the inline-after-save adapter pattern used for HEAppE sync. Outbound email/HTTP is therefore present in the module's adapter layer, by design rather than via the event bus.
- Accept/reject become **project-scoped** — a behaviour change from legacy that closes a cross-project authorization hole; a caller without the manage role on the request's own project gets `404`.
- Self-onboarding create keeps both legacy guards (already-an-active-member, duplicate-pending-request) as domain errors mapped to `409`; settling a Request that is already terminal stays a domain-level conflict (the state machine forbids the transition).
- Emailed links resolve through the Gateway to the v2 handle route; **in-flight legacy links are unaffected** (legacy endpoint remains), but the two stacks now mint links at different bases.
