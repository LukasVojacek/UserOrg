# 0065 — Draft ProjectResource: ProviderAdmin pre-creates, the create-resource flow materializes

Date: 2026-07-14
Status: Superseded by [0085](0085-resource-request-legacy-input-shape-draft-removed.md)
Issue: #251 (replaces #232's mode:"new" discriminated create)

## Context

Legacy supported requesting a resource that did not exist yet (`PendingForHPCProject` limbo keyed by a free-text
name + requester-supplied PI email). #232 kept a `mode:"new"` create with `piEmail` input. Requirement changed:
the requester must never supply a PI email; a resource record should exist up front, in a state where it is
not yet usable.

## Decision

- `ProjectResource` gains `Availability { Draft, Active }` (existing rows backfilled `Active`).
  **ProviderAdmin** pre-creates Draft records via `POST providers/{providerName}/resource-drafts`
  (name, PI email, description/validity) — catalog intake. Requests always reference an existing record;
  the PI email always comes from the record.
- A Draft is **materialized by the existing `create-project-resource` workflow**: `POST
  providers/{providerName}/resources` with a name matching a same-provider Draft materializes it
  (allocations + HEAppE init + `Active`) instead of returning 409. This is the only activation path.
- Materialization raises `ProjectResourceActivatedEvent` (durable); a Core handler resumes every assignment
  request parked on the `ResourceAvailability` gate for that resource key.
- Guards: the direct F5 attach endpoint refuses Draft resources (422); allocation assignment to a Draft is
  allowed only through the provisioning/materialization path.
- Persistence split per repo convention: the new column on the legacy-shared `ProjectResources` table ships
  via the legacy Infrastructure.DataAccess migration; the new projection table is a schema-isolated modular
  unit (ADR-0047 pattern).

## Consequences

- The legacy "request names a non-existent resource" limbo disappears; every request is anchored to a row
  with a known PI. Provider communication happens on approve-of-draft (email to provider support) and the
  wait is an explicit engine gate, not a status flag nobody owns.
- Draft visibility in resource lists must be explicit (`availability` field) so UIs can distinguish
  requestable-but-pending capacity from live resources.
