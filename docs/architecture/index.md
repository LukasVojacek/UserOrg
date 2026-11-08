# Architecture Decision Records (ADR)

This directory contains ADRs describing major architectural decisions for the UserOrg project. Each ADR includes alternatives and the rationale for the chosen implementation as reflected in the current codebase.

## ADR List

- 0001 - Gateway: internal short‑lived JWTs and Service‑to‑Service (S2S) tokens — accepted
- 0002 - Reverse proxy: In‑process YARP (Yet Another Reverse Proxy) — accepted
- 0003 - Secrets: HashiCorp Vault via proxy agent — accepted
- 0004 - Persistence: SQL Server (EF Core) + immudb for immutable audit — accepted
- 0005 - Permission mapping: central endpoint→permission mapping (Forge.Core) — accepted
- 0013 - WorkflowEngine is a standalone bounded context — accepted
- 0014 - WorkflowEngine: first-class suspend/resume, not retry-as-poll — accepted (implemented)
- 0015 - WorkflowEngine: fluent builder DSL as the public flow-definition API — accepted (implemented)
- 0088 - Audit-log PoC: log-only sink now, immudb stays the target (narrows ADR-0004) — accepted
- 0089 - A partial dependency scan fails the job — accepted
  ([`adr/0089-a-partial-dependency-scan-fails-the-job.md`](adr/0089-a-partial-dependency-scan-fails-the-job.md))
- 0090 - Reset-all-caches uses the reserved wildcard tag, not per-producer enumeration — accepted
- 0091 - Merge of `origin/main` into `feat/refactor-core`, round 2: repair a GitLab-squashed
  lineage — accepted
- 0092 - Propagate the original token id (`original_jti`) through the internal token — accepted
- 0093 - Log the token id in every component — accepted
- 0100 - The in-memory workflow queue is best-effort; the `workflow.Requests` row is the durability
  boundary — accepted
- 0101 - Workflow step payloads carry natural keys and request DTOs, never aggregates — accepted
  ([`adr/0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](adr/0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md))
- 0102 - AppHost dashboard command placement — accepted
  ([`adr/0102-apphost-dashboard-command-placement.md`](adr/0102-apphost-dashboard-command-placement.md))
- 0103 - A v2 response carries a soft-delete flag only where its read bypasses the soft-delete filter —
  accepted
  ([`adr/0103-v2-carries-a-soft-delete-flag-only-where-the-read-bypasses-the-filter.md`](adr/0103-v2-carries-a-soft-delete-flag-only-where-the-read-bypasses-the-filter.md))
- 0105 - Provider write authority is the Admin role or the caller's own provider — accepted
  ([`adr/0105-provider-write-authority-is-the-admin-role-or-the-callers-own-provider.md`](adr/0105-provider-write-authority-is-the-admin-role-or-the-callers-own-provider.md))
- 0106 - Gateway: a route-existence oracle derived from the published OpenAPI surface, accepted
  ([`adr/0106-gateway-route-existence-oracle.md`](adr/0106-gateway-route-existence-oracle.md))
- 0107 - One caller-centric onboarding request list with a direction filter - accepted
  ([`adr/0107-caller-centric-onboarding-request-list.md`](adr/0107-caller-centric-onboarding-request-list.md))
- 0108 - Role-management operators sign in through the IdP, superseded by 0110
  ([`adr/0108-role-management-operators-sign-in-through-the-idp.md`](adr/0108-role-management-operators-sign-in-through-the-idp.md))
- 0109 - The audit log covers every P1 and P2 operation, accepted
  ([`adr/0109-audit-covers-every-p1-and-p2-operation.md`](adr/0109-audit-covers-every-p1-and-p2-operation.md))
- 0110 - The role-management console signs in with a shared access token, accepted
  ([`adr/0110-role-management-console-signs-in-with-a-shared-access-token.md`](adr/0110-role-management-console-signs-in-with-a-shared-access-token.md))
- 0111 - A paged read with one child collection may page a single-query projection, accepted
  ([`adr/0111-a-paged-read-with-one-child-collection-may-page-a-single-query-projection.md`](adr/0111-a-paged-read-with-one-child-collection-may-page-a-single-query-projection.md))
- 0112 - Logging modes: Warning normally, Information on step-up, time-limited diagnostic Debug, accepted
  ([`adr/0112-logging-modes.md`](adr/0112-logging-modes.md))
- 0113 - Membership changes reach the gateway through a response header, accepted
  ([`adr/0113-membership-changes-reach-the-gateway-through-a-response-header.md`](adr/0113-membership-changes-reach-the-gateway-through-a-response-header.md))
- 0114 - Both hosts apply forwarded headers from trusted proxies only, accepted
  ([`adr/0114-trusted-proxy-forwarded-headers.md`](adr/0114-trusted-proxy-forwarded-headers.md))
- 0115 - Required child collections in the v2 model map ClientCascade, accepted
  ([`adr/0115-required-child-collections-map-client-cascade.md`](adr/0115-required-child-collections-map-client-cascade.md))
- 0116 - An unknown sealing key is retry or dead-letter by cause, and the producer's key cache follows the proxy's store, accepted
  ([`adr/0116-unknown-sealing-key-is-retry-or-dead-letter-and-the-producer-cache-follows-the-store.md`](adr/0116-unknown-sealing-key-is-retry-or-dead-letter-and-the-producer-cache-follows-the-store.md))
- 0117 - Forge.Core exposes no HTTP endpoints; RBAC administration is the gateway console, accepted
  ([`adr/0117-forge-core-exposes-no-http-endpoints-rbac-admin-is-the-gateway-console.md`](adr/0117-forge-core-exposes-no-http-endpoints-rbac-admin-is-the-gateway-console.md))
- 0118 - Every HEAppE call goes through the NexusApi proxy, addressed by the HEAppE identifier, accepted, partially superseded by 0119
  ([`adr/0118-heappe-calls-go-through-the-nexusapi-proxy-by-identifier.md`](adr/0118-heappe-calls-go-through-the-nexusapi-proxy-by-identifier.md))
- 0119 - The caller's token is exchanged per HEAppE instance behind TokenExchangeEnabled, accepted, partially superseded by 0119
  ([`adr/0119-caller-token-is-exchanged-per-heappe-instance.md`](adr/0119-caller-token-is-exchanged-per-heappe-instance.md))
- 0120 - NexusApi settings live under TokenExchange:Nexus, and the token exchange flag gates the registrations at startup, accepted
  ([`adr/0120-nexusapi-settings-under-token-exchange-and-a-startup-gate.md`](adr/0120-nexusapi-settings-under-token-exchange-and-a-startup-gate.md))
- 0121 - The gateway rejects user tokens that carry an unconfigured audience, accepted
  ([`adr/0121-gateway-rejects-user-tokens-with-unconfigured-audiences.md`](adr/0121-gateway-rejects-user-tokens-with-unconfigured-audiences.md))

> Note: ADRs 0006 to 0012 and 0016 to 0087 (with gaps, 0068 is unused) exist in this directory but
> predate this index; only 0001 to 0005, 0013 to 0015, 0088 to 0093, 0100 to 0103, 0105 to 0117 are listed here.
> `0104` is claimed by work not yet on `main`.

> Note: several numbers in `adr/` are used by more than one file, so a bare `ADR-NNNN` citation is
> ambiguous for them. The collisions known today span **0053**–**0066** and **0085**; the list is not
> guaranteed complete, and `ls adr/ | sed 's/-.*//' | sort | uniq -d` is the authoritative check. Cite
> any colliding number by full filename. Resolving the remaining collisions is a separate
> `architecture-docs` audit; only `0094` has been resolved so far (the dependency-scan decision moved to
> `0089`, leaving `0094` to
> [`adr/0094-interactive-provider-admin-identity-and-the-write-asymmetry.md`](adr/0094-interactive-provider-admin-identity-and-the-write-asymmetry.md)).

## Conventions

See `docs/architecture/module-conventions.md` for the three-project module layout, the feature structure
inside `src/Modules/Core/`, and the composition, endpoint, domain model and error handling conventions the
refactored core follows.

## Diagrams

See `docs/architecture/diagrams.md` for mermaid diagrams illustrating the component topology, gateway authorization flow, and Vault flow.

## Risks

See [`07-risks.md`](07-risks.md) for the open architecture risks, their owners and status.

## How to update

1. Add a new ADR file to this directory (follow the numeric sequence `0006-...`).
2. Update this index with the new ADR and its status.
