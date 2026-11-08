# 0066 — Resource-request public identifier: `{project}-{resource}` slug with single-active invariant

Date: 2026-07-14
Status: Accepted
Issue: #251 (extends ADR-0024 GUID-free slugs)

## Context

v2 public contracts avoid GUIDs (ADR-0024). The developer wants the request addressed by a human-readable
slug derived from the project and resource. A PI-email-based slug was rejected: email in a URL is PII
(logs, browser history, referrer).

## Decision

- Slug = `{projectShortName}-{resourceName}`, lower-kebab normalized; **unique column** on the projection.
- **Invariant: at most one non-terminal request per (project, provider, resource)** — enforced by a filtered
  unique index; a duplicate create returns 409.
  **Amended by [0085](0085-resource-request-legacy-input-shape-draft-removed.md)**: `resource` is no longer a
  catalog-record-anchored identity — `ResourceName` is requester-supplied free text (DEC-1), so `cluster-x` and
  `Cluster-X` can be two distinct active requests for one intent. Accepted (`ponytail:` — index collation not
  worth changing before anyone hits it). The filter itself is an allow-list of the non-terminal states
  (`[State] IN (N'PendingPiApproval', N'AwaitingResourceCreation')`), so the terminal state list below does not
  need to appear in the index and a new terminal state needs no migration to release the slot.
- A new request whose base slug is already taken appends an ordinal suffix (`…-2`, `…-3`). This covers
  both repeats after a terminal request (Rejected/Cancelled/Assigned/Failed/Expired — the last added by
  [0085](0085-resource-request-legacy-input-shape-draft-removed.md), DEC-7) and the cross-provider case —
  resource identity is (provider, name) per ADR-0026/0044, so one project may legitimately hold active
  requests for same-named resources at two providers; the suffix keeps every slug unique while the
  single-active invariant stays 3-part (project, provider, resource). History stays addressable, old links stable.
- No PII in any identifier; the PI email lives only as data on the projection row / resource record.

## Consequences

- Slugs are meaningful in UIs and logs; collision handling is deterministic and testable.
- Renaming a project/resource does not rename existing slugs (they are identifiers, not lookups).
