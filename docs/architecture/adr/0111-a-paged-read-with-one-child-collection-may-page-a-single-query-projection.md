# ADR 0111 - A paged read with one child collection may page a single-query projection

- Date: 2026-09-29
- Status: accepted
- Relates: ADR-0103 (the soft-delete flag the same read carries)

## Context

`GET /api/v2/ProviderAdmin/Providers` lists administered providers, each with its locations, one page at a
time. The same provider-with-locations shape already exists for `GET /api/v2/Providers/{providerName}`,
built by a projection with a correlated location subquery in
`src/Modules/Core.DataAccess/Queries/ProviderDetailsQuery.cs`.

Every paged read in `src/Modules/Core.DataAccess/Queries` that returns a child collection so far is two-step:
it pages flat rows, loads the children in a second query and stitches them with `Grouping.GroupToLists`
(`src/Modules/Core.DataAccess/Queries/Grouping.cs:10`; used at `ProjectDetailsQuery.cs:365` and
`DdiProjectsInfoQuery.cs:152`, `:206`, `:208`). None pages a projection that carries a collection, and no
`QuerySplittingBehavior` is configured, so EF Core's default single-query mode applies.

Following the convention here would duplicate the location projection (column list, order, soft-delete
filter) between the two provider reads and add a query per page.

## Decision

**A paged read with exactly one child collection may page a single-query projection through
`DataQuery.ToPagedListAsync`.** `GetAdministeredProvidersAsync`
(`src/Modules/Core.DataAccess/Queries/ProviderDetailsQuery.cs:36-54`) pages the same private projection
`SelectProviderDetails` (`:56-79`) that `GetProviderDetailsAsync` (`:12-34`) reads, so the location list, its
order by `Name` and the soft-delete filter live in one place. Paging orders by `Name`, then the twin
resolution order (`ProviderTwinOrder.ThenByTwinResolution`, ADR-0027 amendment; ends on the shadow `Id`), so a
page boundary never splits or repeats a provider.

Evidence: a prototype on SQL Server 2022 with EF Core 10.0.12 translated the count to
`SELECT COUNT(*) FROM [Provider]` (the projection dropped) and the page to one statement that pages providers
in a subquery and `LEFT JOIN`s `Location`, with no EF warnings.
`Tests/E2E/IntegrationTests/AuthMatrix/ProviderAdminProvidersListTests.cs` runs the read through the Gateway
against SQL Server in the manual container job (`-p:RunContainerTests=true`). On the twin resolution order it
ran green on 2026-09-29 (T-USR-4-01, `test-filter ProviderAdminProvidersListTests`: `total: 4, failed: 0`).

Rejected:

- Two-step flat rows plus `Grouping.GroupToLists`: the repo's paging convention, but it duplicates the
  location projection and adds a query per page.
- `AsSplitQuery()`: a second statement for no gain with a single collection.

## Scope

- The existing two-step reads through `Grouping.GroupToLists` stay as they are; this ADR does not ask to
  migrate them.
- It applies only to a read with exactly one child collection, and it is verified on SQL Server only.
- A read with a second collection reverts to two-step or `AsSplitQuery()`: two collections in one statement
  multiply rows (a cartesian explosion) and EF Core warns about it.

## Consequences

- One projection serves both provider reads, so `GET /api/v2/Providers/{providerName}` now returns its
  locations ordered by `Name` as well.
- **Revisit trigger:** a second collection added to `SelectProviderDetails` raises EF Core's multiple-collection
  warning and must reopen this decision before it merges.
