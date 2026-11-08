# ADR 0007 - Project tags are addressed by name, with create separated from assign

- Date: 2026-06-03
- Status: accepted

## Decision

In the v2 Project API a **Tag** is addressed by its **name** (the catalogue key), not by the surrogate `Guid` the legacy API exposed. Minting a new catalogue Tag and assigning an existing Tag to a project are treated as **two distinct authorities**, mapped to the two permissions the RBAC model already defines:

- `POST v2/Projects/{projectShortName}/Tags` — **create-and-assign.** If the named Tag is absent from the global catalogue it is created and assigned to the project; if it already exists it is assigned. Gated by `perm:project:tag:create`, held only by **Owner, Manager, Admin**.
- `PUT v2/Projects/{projectShortName}/Tags/{tagName}` — **assign existing only.** Returns `404` when no Tag with that name exists; it never creates one. Idempotent. Gated by `perm:project:project:tag:assignment`, held by all project roles.
- `DELETE v2/Projects/{projectShortName}/Tags/{tagName}` — removes the assignment (join row) only; the shared Tag is never deleted. Idempotent. Same `tag:assignment` gate.

Authorization stays entirely at the Gateway/RBAC layer: each path+method maps statically to one permission. Core does not perform a second, runtime role check.

## Context

- A Tag is a single entry in a **global, shared catalogue** keyed by name (see CONTEXT.md). Assigning the same name from two projects references the one catalogue entry; it does not create two.
- The legacy model already separates `perm:project:tag:create` (catalogue management, `POST /api/ProjectTag`) from `perm:project:project:tag:assignment` (the project↔tag link). Owner, Manager and Admin hold create; lesser project roles hold only assignment.
- An earlier draft of the v2 migration collapsed both into a single `PUT .../tags/{tagName}` that auto-created the Tag under the `tag:assignment` permission. That let any caller able to assign a tag mint new global catalogue entries — bypassing `tag:create` and letting typos pollute the shared catalogue for every project.

## Considered options

- **Single auto-create endpoint with a runtime role check in Core** (endpoint gated by `tag:assignment`; when the Tag is absent, Core additionally verifies the caller is Owner/Manager for the project via the `projectRoles` claim). Rejected: RBAC is endpoint-granular and cannot express "require a different permission when the tag is missing", so this splits authorization across two layers and hides the create intent from the Gateway. Keeping one authorization layer is cleaner.
- **Keep addressing tags by `Guid`** (legacy parity). Rejected: v2 deliberately exposes natural/business identifiers instead of surrogate database keys; a name is the stable client-facing identity for a tag.

## Consequences

- Assigning a name with no catalogue Tag now returns `404` (legacy parity for the assign path) rather than silently creating it. Clients that want a new tag use `POST .../tags`, which requires `tag:create`.
- The Gateway RBAC map must carry the v2 tag endpoints with their respective permissions (`POST → tag:create`, `PUT/DELETE → tag:assignment`); a single `tag:assignment` mapping for all three would re-open the escalation.
- Tag names are now a client-facing contract. Renaming a catalogue Tag is a breaking change for callers that reference it by name; uniqueness of the name is required for the lookup to be deterministic. Both the catalogue `Tag` (`UX`/alternate key on `ProjectTag.Name`) and the `ProjectDomainType` lookup (`UX_ProjectDomainType_Name`) enforce that uniqueness at the database.

## Related: domain type by name

The same "natural name instead of surrogate key" rationale applies to the v2 update-project endpoint, which now takes `DomainTypeName` (string) instead of the legacy `int DomainTypeId`. This is lower-risk than the tag change — `ProjectDomainType` is a closed, admin-managed lookup table with a unique name, and v2 ships a discovery endpoint (`GET v2/ProjectDomainTypes`) so clients can enumerate the valid names. It does not warrant its own ADR but is recorded here as the second instance of the same decision.
