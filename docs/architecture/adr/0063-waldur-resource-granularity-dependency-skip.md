# Dependency skip at resource granularity, keyed by offering→customer

**Status:** accepted (grilling session 2026-07-17) · relates to ADR-0062, ADR-0057 · legacy parity: `ProjectSyncSkipRegistry`

## Context

Per-item fetch isolation (ADR-0062) can leave a provider carried-forward with stale or absent
offerings. A resource is tied to its provider/location only through its **offering** (a resource
carries `offering_uuid`; the owning customer is `offering.customer_uuid`). If an offering is
unavailable because its customer's offering fetch failed, resources on that offering cannot be
fully mapped.

The obvious "block dependents of a failed parent" design — a provider→project parent link — does
**not fit the data**: a project has no producer-level customer reference (`ApiProject` carries no
customer; `GetProjectsAsync` is a global fetch), and a single project's resources may belong to
**several** customers. A project is not owned by one provider.

Legacy did not block at project→provider either. `ProjectSyncSkipRegistry` skipped a project when
it had "no eligible resources or partial offering data", and `UserConsumer` then skipped that
project's membership — i.e. the skip unit was the **resource/offering**, and a project fell out
only when it had nothing eligible left.

## Decision

Proactively skip at **resource granularity**, keyed by offering→customer availability, during
mapping in `BuildProjectsAsync`:

- A resource whose offering belongs to a customer whose offering fetch failed (the offering is
  missing from `offeringsById`) is **excluded** from the project's `Resources`.
- A project left with no eligible resources is **carried forward** (`Payload: null`, ADR-0062),
  matching legacy's "no eligible resources → skip project".
- No artificial provider→project parent link is introduced; the skip is a mapping-time decision,
  not a diff-time cascade.

Membership is **not** blocked by a carried-forward project. Members do not depend on resources,
and the consumer's `RequireProject` guard keeps emission safe (a never-created project throws →
no ack → replay). This diverges from legacy's membership skip, deliberately, to keep correct
member state flowing for projects that already exist.

## Consequences

- Referential integrity is preserved without a project→provider model the data cannot support.
- The consumer `Require*` guards (ADR-0057, now Result per ADR-0065) remain the load-bearing
  integrity net: a resource whose location was never created is blocked at the consumer and
  replays; a resource whose location exists from a prior iteration links to it (legacy-faithful).
- Excluded resources are logged at Debug (ADR-0066); the project-level effect is visible via the
  carried-forward null payload.
