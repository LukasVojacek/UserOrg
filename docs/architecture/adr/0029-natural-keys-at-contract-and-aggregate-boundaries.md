# ADR 0029 - Natural keys at contract and aggregate boundaries; surrogate ids stay inside read-query internals

- Date: 2026-06-27
- Status: accepted (2026-06-27) — formalises a rule applied while finishing the F0.3 read-scope tiers (GetProviders + by-id endpoints).
- Relates to: ADR-0018 (read-scope tiers; identical-404 hiding), ADR-0027 (aggregate-first natural-key uniqueness), ADR-0026 (per-provider ProjectResource identity). Constrains all `Modules/Core` endpoints and queries.

## Context

Internally every entity has a surrogate key — a `Guid` aggregate id and, for entities the write aggregate
doesn't navigate to, an EF **shadow** FK (e.g. `Provider.Id`, `Location`'s `ProviderId`/`LocationId`). It is
tempting to let those leak outward: route a by-id endpoint on a `Guid`, or pass a surrogate id between
aggregates because the join is right there. The F0.3 work made the cost concrete — `GetLocation` by-id had
drifted into a two-step `GetIdByProviderAndNameAsync → GetLocationAsync(Guid)` dance, threading a surrogate
through application code that the contract never exposed.

Surrogate ids are a **persistence implementation detail**. Exposing them in a public contract freezes that
detail into the API; passing them across aggregate boundaries on the write side couples aggregates to each
other's storage instead of to their domain identity (the natural key).

## Decision

Natural keys are the only identifiers that cross a boundary; surrogate ids are confined to read-query
internals.

1. **Endpoint contracts (Request/Response) carry natural keys only** — `providerName`, `locationName`,
   `projectShortName` — never a `Guid` or shadow id. A by-id endpoint is "by natural key".
2. **Read-query *inputs* are natural keys**, because that is all the contract supplies. A query resolves the
   natural key to whatever surrogate it needs *inside* the query.
3. **Read-query *internals* may map across relationships via surrogate ids** — joins on shadow FKs, pre-resolving
   a set of ids to constrain a paged read (e.g. the F0.3 provider-reachability chain resolves a `Distinct` set of
   shadow `ProviderId`s and ORs it into the page predicate). These ids never appear in the input or the output.
4. **Write side — aggregate methods and cross-aggregate references use natural ids**, resolved through a
   domain-owned port (ADR-0027 pattern), never a surrogate id handed across the boundary.

## Consequences

- By-id reads resolve through a single natural-key (+ scope) query, mirroring the existing
  `GetResourceByProviderAndNameAsync`. The `GetLocation` two-step surrogate round-trip is *removed*, not extended,
  when its user-scope path is added.
- A query whose contract input is a natural key may still need an internal lookup to a surrogate before the main
  read — accepted; it is the price of not leaking the surrogate.
- The rule is a boundary rule, not a ban on surrogate keys: storage, EF mappings, and read-query joins keep using
  them freely. Reviewers check the **edges** (contract DTOs, query signatures, aggregate-method parameters), not
  the query bodies.

## Considered options

- **Surrogate ids in by-id routes (rejected).** Simplest routing (`/providers/{guid}`), no natural-key
  resolution step — but freezes a storage detail into the public contract and breaks the identical-404 hiding
  story (a `Guid` route can't be "out of scope vs missing"-ambiguous the way a natural key is). ADR-0018 needs the
  natural key.
- **Pass surrogate ids between aggregates on the write side (rejected).** Convenient when a join is in reach, but
  couples aggregates to each other's persistence and bypasses the port-based resolution that ADR-0027 relies on
  for invariant enforcement.
