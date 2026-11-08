# ADR 0109 - The audit log covers every P1 and P2 operation

- Date: 2026-09-23
- Status: accepted

## Context

[ADR-0088](0088-audit-log-poc-narrows-adr-0004.md) introduced the audit log with two call sites, and
[ADR-0096](0096-audit-log-ships-sealed-to-immudb-through-an-unsealing-proxy.md) made it durable. The
v2 audit analysis (`v2-audit-log-analysis.md` section 4) ranks every v2 operation as P1 (security and
access, the anonymous token paths, gateway and RBAC administration), P2 (domain writes, operational and
infrastructure events) or P3 (reads). The human decision D2 put every P1 and P2 operation in scope and
left P3 out. This ADR fixes what that coverage writes, because every action name it introduces lands in
an append-only log and cannot be renamed later.

## Decision

### The action catalog

Every action is three dot-separated segments, `area.entity.verb`, with a past-tense verb and snake_case
inside a segment (`src/Modules/Core/Shared/Auth/AuditActions.cs`). The catalog below is fixed and
append-only.

- Core: `project.user.assigned`, `project.user.unassigned`, `project.self.created`,
  `project.self.updated`, `project.self.deleted`, `project.tag.created`, `project.tag.assigned`,
  `project.tag.unassigned`, `project_tag.self.deleted`, `project.resource.assigned`,
  `project.resource.unassigned`, `user.provider_admin.granted`, `user.provider_admin.revoked`,
  `user.preferences.updated`, `user.self.provisioned`, `onboarding_request.self.accepted`,
  `onboarding_request.self.rejected`, `request.self.created`, `request.self.accepted`,
  `request.self.rejected`, `request.action_link.settled`, `request.action_link.viewed`,
  `resource_request.self.created`, `resource_request.self.cancelled`,
  `resource_request.approval_link.reissued`, `resource_request.action.viewed`, `provider.self.created`,
  `provider.self.updated`, `provider.self.deactivated`, `provider.self.reactivated`,
  `provider.location.added`, `provider.location.updated`, `provider.location.removed`,
  `provider.resource_assignment.created`, `provider.resource_assignment.updated`,
  `provider.resource_assignment.removed`, `resource.creation.requested`, `resource.self.updated`,
  `resource.self.deleted`, `resource.self.published`, `cache.output.flushed`. The existing
  `project.user_role.changed` and `resource_request.action.settled` are unchanged.
- Gateway: `gateway.authorization.bypassed`, `gateway.role_management.signed_in`,
  `gateway.role_management.signed_out`, `gateway.rbac_role.created`, `gateway.rbac_role.updated`,
  `gateway.rbac_permission.created`, `gateway.rbac_permission.deleted`,
  `gateway.endpoint_mapping.created`, `gateway.endpoint_mapping.updated`,
  `gateway.endpoint_mapping.deleted`, `gateway.rbac.imported`, `gateway.rbac.exported`,
  `gateway.rbac.seeded`, `gateway.signing_key.generated`, `gateway.signing_key.rotated`,
  `gateway.api_key.authenticated`, `gateway.dev_override.authenticated`. The existing
  `gateway.authorization.denied` is unchanged.
- Host: `feature_flag.request.blocked`.

When an operation acts on the area's own aggregate, the entity segment is the literal `self`:
`project.self.deleted`, `provider.self.created`, `onboarding_request.self.accepted`. `self` means "the
area's own aggregate" and nothing else. The gateway catalog has no aggregate of its own, so it has no
`self` entry.

Core declares the whole catalog in `AuditActions.cs` in one change, with the `DenialReasons` and
`TargetTypes` the later call sites use, so no later change edits that file. The gateway catalog lives in
`GatewayAuditActions.cs`, and the host middleware keeps a small catalog of its own because it cannot see
Core internals.

### Outcomes and reasons

A Core endpoint writes Success, and Denied when an authorization check refuses the caller: a masked 404
from a scope check (reason `project.caller_out_of_scope`), an explicit 403, or an unresolvable or spent
anonymous token. A 400, a genuine 404, a 409 and a 422 write nothing, following
`ChangeProjectUserRoleEndpoint`, where a conflict recorded as Denied would be a false positive forever.
Failure is written only by callerless background work: RBAC seed reconciliation and signing-key rotation.

A reason is a stable code, never prose. It reuses an existing problem code or a domain `Error` code where
one exists and is declared once, in `AuditActions.DenialReasons` for Core. `Data` carries identifiers
only, never an e-mail, a name or free text, and `OldValues`/`NewValues` stay unused (ADR-0088 section 5).

### Fail-closed and fail-open

The call site decides the criticality (ADR-0088 section 4). A P1 site is fail-closed: the audit write is
the handler's last statement, after `SaveChangesAsync` and cache eviction, and an exception from it
propagates. A P2 site is fail-open: it writes through `AuditBestEffortAsync`
(`src/Modules/Core/Shared/Auth/AuditLoggerExtensions.cs`, and a gateway twin), which awaits the write and
swallows any exception.

A swallowed failure logs one Warning naming the action, the outcome and the exception type (human
decision D10). It never logs the exception message or the event's data, because either can carry
personal data. The library's `audit_write_failures_total` counter moves as well, but the counter alone is
not treated as enough.

### The RBAC seed writes fail-open

`gateway.rbac.seeded` is written fail-open (human decision D11), although the analysis lists RBAC seeding
as P1 and ADR-0088 pairs P1 with fail-closed. The seed runs at startup with no caller, and fail-closed
there would mean refusing to start the gateway when the audit sink is down. ADR-0088 leaves criticality
to the call site, and the console's RBAC mutations stay fail-closed through their decorator.

### Deliberate non-audits

- The gateway's empty-permission-template pass is not audited. It is the designed posture of 27 seeded
  mappings, among them `/api/v2/userinfo`, so a record there would be written on nearly every page load.
  A mapping being emptied is caught by `gateway.endpoint_mapping.updated`. The missing-permission-service
  pass is audited as `gateway.authorization.bypassed`.
- Internal token issuance is not audited per token. Minting runs for every proxied request behind a
  30-second cache. Signing-key generation and rotation are audited instead.
- A primary API-key success is not audited, because every service call succeeds through it. Every failed
  API-key authentication and every backup-key success is audited.
- A 401 for a request that presents no credential at all is not audited (#333). Anyone who reaches the gateway
  without a token produces one, so the records would be noise that crowds out the denials worth reading.

## Consequences

- The anonymous audit-write surface grows from one route to four: the request action link settle and
  preview, and the resource-request token preview beside the existing settle. None of them is rate
  limited, so an unauthenticated client can make the gateway write audit records. This is an accepted
  risk.
- A full spool makes the sink return `Dropped` rather than throw. Neither the D10 Warning nor a
  fail-closed P1 call site sees that loss. Only the counters and the spool health check's
  `SpoolFullStatus` do.
- A fail-closed write that throws after the commit answers the caller with a failure for a change that
  did commit. That is the accepted ADR-0088 consequence, and it is why the write follows cache eviction.

### Follow-ups

- Rate limiting on the anonymous routes.
- Alerting on `audit_write_failures_total` (a sink write that threw), on
  `audit_events_dropped_total{outcome}` with `audit_spool_rejected_full_total` (records the full spool
  discarded without throwing), and on `audit_encryption_failures_total` (sealing failures, which also
  throw).

## Considered options

- **Repeat the aggregate in the entity segment** (`project.project.deleted`). Rejected: it reads as a
  typo and gets queried wrongly.
- **Emit from the outbox or from SQL.** Rejected: ADR-0088 sections 1 and 3 keep emission at the
  endpoint seam, where `Caller` is available and refusals that never reach a domain event are visible.
- **Rely on the library's failure counter alone for a fail-open write.** Rejected by D10: a counter
  names no action, so nobody can tell which operation lost its record.
