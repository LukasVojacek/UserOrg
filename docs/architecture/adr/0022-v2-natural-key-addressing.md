# ADR 0022 - v2 endpoints address resources by natural key, not surrogate GUID

- Date: 2026-06-15
- Status: accepted (partial implementation — see Scope)

## Decision

A v2 endpoint addresses a resource by its **natural key** whenever the aggregate has one. Surrogate database
GUIDs are not accepted as lookup input on the public surface. Concretely:

| Aggregate | Natural key | v2 route |
|---|---|---|
| Provider | name | `Providers/{providerName}` (already) |
| Project | short name | `Projects/{projectShortName}` (already) |
| Resource | **provider + name** (the name is unique only *per provider*) | `Providers/{providerName}/Resources/{resourceName}` |
| Tag | name (unique across the system) | `ProjectTags/{tagName}` |
| Location | provider + name (belongs to exactly one Provider) | `Providers/{providerName}/Locations/{locationName}` |

Because both Location's and Resource's natural keys span their Provider, their reads become **nested routes**,
not flat renames.

> **Amendment (2026-06-16).** The Resource row originally read "name → `resources/{resourceName}`". A flat
> `resourceName` is **not** a unique natural key: the same resource name can exist under different providers,
> so a flat by-name read returns an arbitrary match on collision. The natural key is therefore the pair
> `(providerName, resourceName)`, and both the list and the single read are nested under the provider
> (`providers/{providerName}/resources[/{resourceName}]`). Addressing a name under the wrong provider returns
> the 404-hiding `ProblemDetails`, same as a genuinely-missing name. The list stays caller-scoped (user-space
> only; the provider path narrows, it does not widen to admin/provider-wide reads).

**Where no natural key exists, the surrogate GUID is retained** as the addressing key. A **Request** has no
natural identifier, so accept/reject keep addressing it by `requestId` (GUID). This is the deliberate
"where possible" escape hatch, not an oversight.

The **404-hiding rider (ADR-0018)** applies to natural-key lookups unchanged: a name that is genuinely
unknown and a name hidden because it is outside the caller's read scope return the **identical**
`ProblemDetails` (same code, title, detail), so addressing by name leaks no more existence than addressing
by id did.

## Scope (what is and isn't done now)

**In scope / to implement:** the three by-id reads above (`GetResource`, `GetProjectTag`, `GetLocation`)
switch from `{id:guid}` to their natural key, via additive by-name query methods plus the route change. v2 is
unreleased, so changing these routes breaks no consumer.

**Explicitly deferred (revisit later):** to avoid destructive changes to the in-flight DDD migration, this ADR
does **not** yet (a) strip surrogate GUIDs from response bodies, (b) convert foreign-key surrogate ids in
responses (`RequestSummary.ProjectId/UserId/ProviderId`, `UserSummary.AssignedProviderId`) to the referent's
natural key — which would force the read queries to join and project those naturals, or (c) convert GUID
list-filters (`GetRequests ?projectId/?userId`) to natural keys. These remain GUID for now and are a separate,
later decision once the migration has settled.

## Context

A Minimal API contract review found three v2 reads (`GetResource`, `GetProjectTag`, `GetLocation`) still
addressed by surrogate `{id:guid}`, and noted they also lacked ADR-0018 read-scope tiering. Rather than bolt
scope-tiering onto GUID lookups, the cleaner fix removes the surrogate-id lookup class entirely: natural keys
are stable domain identifiers already in `CONTEXT.md`, make URLs self-describing, and avoid leaking database
surrogate keys onto the wire. The deeper "purge every surrogate id from the contract" was considered and
deferred because converting relationship references to natural keys is join-heavy and risky to attempt while
the feature-by-feature migration is still in progress.

## Considered options

- **Natural key where possible, GUID where not, full purge deferred (chosen).** Removes the surrogate-lookup
  class on the three reads with no destructive query/response rework; keeps `requestId` where unavoidable.
- **Full purge now** (strip response ids, FK→natural everywhere, natural list-filters). Rejected for now:
  join-heavy projection changes across `RequestSummary`/`UserSummary` and response-shape churn are exactly the
  destructive mid-migration changes we want to avoid.
- **Add ADR-0018 scope-tiering to the GUID reads, keep `{id:guid}`.** Rejected: guards the surrogate-lookup
  class instead of removing it; still leaks surrogate keys and keeps the less meaningful URLs.

## Consequences

- `GetLocation` gains a nested route and a by-(provider,name) lookup (the query layer already has
  `GetIdByProviderAndNameAsync` to build on). `GetResource`/`GetProjectTag` gain by-name query methods.
- The deferred items mean the wire still carries some surrogate GUIDs (response ids, FK references, a few
  filters) during the migration window — an accepted, temporary inconsistency, tracked here for later removal.
- New endpoint tests must assert the 404-hiding equivalence for the natural-key lookups (missing name ≡
  out-of-scope name → identical problem).
