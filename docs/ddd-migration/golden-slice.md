# Golden slice — the reference vertical for `src/Modules/Core`

> **Purpose:** one canonical read slice and one canonical write slice, traced end-to-end across all three
> projects, plus a checklist for adding a new endpoint. Copy these shapes; don't invent new ones.
>
> **Companion to** [core-modules-target-architecture-and-plan.md](./core-modules-target-architecture-and-plan.md) §7 (conventions)
> and the root `CLAUDE.md` "Endpoint conventions". This doc shows the conventions *applied to real code*.

The dependency chain is one-way and transitive: **`Core` → `Core.DataAccess` → `Core.Domain`**. A `Core`
endpoint never references `Core.Domain` directly — it sees domain types through `Core.DataAccess`.

---

## 1. Read slice — `GET /v2/Projects/{shortName}`

**Anchor:** [`GetProjectEndpoint`](../../src/Modules/Core/Features/Project/GetProject/GetProjectEndpoint.cs).
Read goes through a **query port**, never a repository, and returns a **Response DTO**, never an aggregate.

```
Core            GetProjectEndpoint.HandleAsync
                  │  caller.Scope.Contains(shortName)        ← authorization gate (claims-first, ADR-0006)
                  ▼
Core.DataAccess  IProjectQuery.GetProjectAsync(...)          ← read PORT (Queries/Contracts)
                  │  ProjectDetailsQuery (IgnoreQueryFilters, untracked)
                  ▼  ProjectDetails                          ← read DTO (Queries/Contracts)
Core            GetProjectResponse.From(projectDetails)      ← projection to Response DTO
                  ▼
                Results.Ok(response)  →  200 | 404
```

| Layer | File | Role |
|---|---|---|
| Endpoint | [GetProjectEndpoint.cs](../../src/Modules/Core/Features/Project/GetProject/GetProjectEndpoint.cs) | `Request` (`[AsParameters]`), `HandleAsync`, `MapGetProjectEndpoint`, `MapDocumentation` |
| Response | [GetProjectResponse.cs](../../src/Modules/Core/Features/Project/GetProject/GetProjectResponse.cs) | `internal sealed record` + static `From(ProjectDetails)` mapper |
| Query port | `IProjectQuery` (Core.DataAccess `Queries/Contracts`) | the only thing the endpoint depends on for reads |
| Query impl | `ProjectDetailsQuery` (Core.DataAccess `Queries`) | EF read, `IgnoreQueryFilters`, untracked; returns `ProjectDetails` |

**Why a Response DTO and not `ProjectDetails`?** The Response is the public OpenAPI contract (`Produces<GetProjectResponse>`)
and is the *one* public type a feature is allowed to expose besides composition. `ProjectDetails` is a read DTO
owned by `Core.DataAccess`; keeping the boundary means the read model can change without breaking the API shape.

---

## 2. Write slice — `PUT /v2/Projects/{shortName}/Users/{idpUserId}`

**Anchor:** [`AssignProjectUserEndpoint`](../../src/Modules/Core/Features/Project/AssignProjectUser/AssignProjectUserEndpoint.cs).
This is the *complete* write shape: resolve everything the mutation needs **before** touching the aggregate,
mutate **synchronously**, save once, evict cache, return `204`.

```
Core            AssignProjectUserEndpoint.HandleAsync
                  │  caller.Scope.Contains(shortName)                  ← authorization gate
                  │  resolve dependencies BEFORE mutating:
                  │    IUserDirectory.FindUserIdAsync(caller)          ← VO lookups / role-list reads
                  │    IRepository<User, IdpId>.FindAsync(target)         are async and live in the
                  │    IRoleListService.ListRoles()  + WellKnownRoles      USE-CASE, never inside the aggregate
                  ▼
Core.DataAccess  IRepository<Project, ProjectShortName>.FindAsync     ← write PORT; hydrates aggregate
                  │  _projectincludes (one include-set; IgnoreQueryFilters(["SystemRoleOnly"]))
                  ▼
Core.Domain      project.AssignUserWithRole(target, assignedBy, roleId)  ← SYNC invariant, returns Result<Error>
                  │    AssignUser(...) + UpsertMemberRole(...) over loaded collections
                  ▼  Result<Error>
Core            if (!assign.IsSuccess) return assign.Error.ToProblem()
                  │
Core.DataAccess  IUnitOfWork.SaveChangesAsync()                       ← single commit; dispatches domain events
Core            cache.EvictAsync(Project / UserInfo tags)             ← invalidate affected output-cache entries
                  ▼
                Results.NoContent()  →  204 | 400 | 404 | 409
```

| Layer | File / symbol | Role |
|---|---|---|
| Endpoint | [AssignProjectUserEndpoint.cs](../../src/Modules/Core/Features/Project/AssignProjectUser/AssignProjectUserEndpoint.cs) | orchestration only; no business rules |
| Write port | `IRepository<Project, ProjectShortName>` | `FindAsync` (hydrate) / `AddAsync` / `Delete` |
| Repo impl | [DatabaseContext.ProjectRepository.cs](../../src/Modules/Core.DataAccess/Contexts/Repositories/DatabaseContext.ProjectRepository.cs) | `_projectincludes` — the single, centralized include-set for the aggregate |
| Aggregate method | [`Project.AssignUserWithRole`](../../src/Modules/Core.Domain/Project/Project.cs#L270) | enforces invariants synchronously over loaded `_memberRoles` / `_assignedUsers` |
| Commit | `IUnitOfWork.SaveChangesAsync` | persists + dispatches `AddDomainEvent(...)` events |
| Cache | `IOutputCacheInvalidator.EvictAsync` + `CacheTags` | evict by tag so reads see the write |

**The load-resolve-mutate rule (D-1/D-2).** Anything async — directory lookups, role-service calls, resolving a
value object — happens in the *endpoint* before the aggregate call. The aggregate method is pure, synchronous,
and unit-testable with no mocks (it mutates collections the repository already hydrated). Never pass an
`I*Handler` or async port into an aggregate method; that debt was removed in P1.

---

## 3. Checklist — adding an endpoint

Create the slice folder `Features/<Feature>/<Verb><Resource>/` and one file `<Verb><Resource>Endpoint.cs`.

**Every endpoint:**
1. `internal static class <Verb><Resource>Endpoint`.
2. `internal sealed record Request(...)` bound via `[AsParameters]` (route/query/body params).
3. `internal static async Task<IResult> HandleAsync([AsParameters] Request, ..., CancellationToken ct)`.
4. Authorization first: gate on `caller.Scope` (or the relevant claim) and return `404`/`403` before any work.
5. `extension(IEndpointRouteBuilder app)` member `Map<Verb><Resource>Endpoint()` — **not** the old `this`
   extension-method syntax. Register it in `Composition/EndpointsExtensions.cs` (the `v2` group stays — Gateway routing).
6. `extension(RouteHandlerBuilder builder)` member `MapDocumentation()` calling `.WithName().WithSummary().WithTags().Produces<>()`;
   add `.Accepts<Request>("application/json")` when there's a body.
7. Visibility: everything `internal` except the `Response` DTO (OpenAPI) and the composition entry points.

**Read branch:**
8. Depend on a query port `I<X>Query` from `Core.DataAccess/Queries/Contracts` — **never** `IRepository`.
9. Define `internal sealed record Response(...)` with a static `From(<XDetails>)` mapper. Return the Response, never a read DTO or aggregate.
10. Add `.CacheOutput(<X>Cache.PolicyName)` if cacheable; the cache class lives in the feature folder (e.g. `ProjectCache.cs`).

**Write branch:**
11. Resolve every async dependency (directory lookups, role list, VO resolution) in the handler **before** the mutation.
12. `FindAsync` the aggregate via `IRepository<TAggregate, TKey>`; call a **synchronous** aggregate method returning `Result<Error>`.
13. On failure, `return error.ToProblem()`. On success, `await unitOfWork.SaveChangesAsync(ct)`.
14. Evict affected output-cache entries via `IOutputCacheInvalidator` + `CacheTags`. Return `204`/`201` as appropriate.
15. Guard mutating routes with `.DisabledWhen("ReadOnlyMode")` where applicable.
16. New invariant? Put it in the aggregate method (sync), add a `XxxErrors` factory in `Core.Domain/<Aggregate>/`, and unit-test it directly on the entity — no mocks.

**Verify:** `dotnet build` green; `dotnet test Tests/Modules/Core/Core.UnitTests/Core.UnitTests.csproj` at baseline.

---

## 4. What *not* to do

- ❌ Return a domain aggregate or a read DTO from an endpoint — always a Response DTO.
- ❌ Inject `IRepository` into a read endpoint — use the query port.
- ❌ Put async / `I*Handler` parameters on an aggregate method — resolve in the use-case, mutate synchronously.
- ❌ Make a feature type `public` unless it's the Response DTO or a composition entry point.
- ❌ Scatter `.Include(...)` calls — extend the aggregate's single `_xincludes` set in its repository partial.
