# ADR 0113 - Membership changes reach the gateway through a response header

- Date: 2026-09-30
- Status: accepted
- Amends: [ADR-0006](0006-core-trusts-internal-token-claims.md) and [ADR-0008](0008-core-owns-project-role-writes.md) (the 30 s staleness after a membership write)
- Relates: [ADR-0094](0094-interactive-provider-admin-identity-and-the-write-asymmetry.md) (unchanged)

## Context

The gateway builds the caller's `projectRoles` claim from a permission snapshot it caches for 30 s under
`userperms:{idpUserId}` (`UserPermissionsCache.cs`). ADR-0006 and ADR-0008 accepted that a membership or role
change reaches the caller only after that snapshot expires. In practice the creator's
`GET /api/v2/Projects/{shortName}` right after `POST /api/v2/Projects` answered 404, because the snapshot still
lacked the new project (#337). The same held for every assign, unassign, role change, accepted request and
deleted project.

The API writes the membership rows and knows exactly whose snapshot is stale. The gateway proxies that same
response. ADR-0008 ruled out a push channel, and a DB read on every scope miss would put the database on the
gateway's hot path.

## Decision

**The API names the affected IdP users in the response, and the gateway that proxied it evicts their
snapshots before returning it** (`src/UserOrg.Shared/Http/PermissionsChangedHeader.cs`,
`src/Modules/Services/Gateway/UserOrg.Gateway/Startup/PermissionsChangedTransformExtensions.cs`).

### The header contract

- `X-UserOrg-Permissions-Changed` holds one comma-separated value of distinct IdP user ids. Above 100 ids or
  4 KB it becomes `*`, which means every snapshot.
- These membership-changing endpoints call `PermissionsChangedHeader.Append` right after `SaveChanges`, before
  the audit write and any notification: the 8 v2 endpoints (CreateProject, AssignProjectUser,
  UnassignProjectUser, ChangeProjectUserRole, AcceptOnboardingRequest, AcceptRequest, HandleActionLink,
  DeleteProject) and the v1 `ProjectEndpoint` membership routes. The v1 project delete sends `*`, because its
  service exposes no member list.
- `Append` keeps the ids in `HttpContext.Items` and writes the header in `Response.OnStarting`, because
  `UseExceptionHandler` clears the response headers before `GlobalExceptionHandler` writes a 500. A
  fail-closed audit write that throws after the commit therefore still carries the header.
- **Capability handshake.** The gateway sets `X-UserOrg-Gateway-Capabilities: permissions-changed` on every
  request it proxies to the API, replacing a client-sent value. The API appends only when the request carries
  it, so an API behind an older gateway never sends ids that would reach a client.
- The capability header is advisory, not a trust boundary. A caller that reaches the API directly and sets it
  gets back only ids it already sent, its own id, or ids from v2 routes that require the gateway's internal
  token.

### The gateway side

- The transform runs on the userorg catch-all forwarder only, after the internal-token transform. The Cyclops
  forwarders do not get it.
- It takes the header off the response first, on any status, so the header never reaches a client.
- It evicts each named snapshot, or every snapshot by the HybridCache tag `userperms` on `*`. The next request
  rebuilds the snapshot and with it the internal token, whose cache key includes the `projectRoles` hash.
- A malformed value (two header values, `*` mixed with ids, an id over 255 characters or with whitespace or a
  control character) evicts nothing and logs a Warning. A failed eviction logs a Warning with the ids and
  never fails the response.

### Deploy and rollback order

Deploy the gateway first and the API second; roll back the API first and the gateway second. The capability
handshake makes either order safe: without the capability the API sends nothing, and a gateway without the
transform never advertises it.

## Quality scenario

**QS-06**: Consistency (read your writes). Source and stimulus: `POST /api/v2/Projects`, then
`GET /api/v2/Projects/{shortName}` by the creator. Environment: the same gateway replica. Response: the
creator's snapshot is evicted before the next request, even if the audit write throws. Measure: 200 on the
first GET, 0 s staleness.

## Consequences

- ADR-0006 and ADR-0008 no longer bound membership staleness by the 30 s TTL on the replica that proxied the
  write. Their decisions otherwise stand.
- With several gateway replicas only the one that proxied the request evicts; the others keep the 30 s window.
  Cross-replica eviction needs a distributed cache or a message bus and is out of scope.
- ProviderAdmin promote and reset (ADR-0094) keep their 30 s window.
- The v1 request accept (`PUT Requests/{RequestId}/Accept`) and action-link handling
  (`POST Service/Handle/{actionLink}`) do not send the header and keep the 30 s window.
- The 30 s TTL is unchanged.

## Rejected

- A gateway heuristic that guesses which requests change memberships.
- A database read on a scope miss.
- Documenting the 30 s staleness as accepted behaviour.
