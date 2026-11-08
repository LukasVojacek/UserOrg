# Module conventions

How the refactored core is laid out and what the code in it is expected to look like. The legacy monolith in
`src/Core/` predates these rules; `src/Modules/Core/` is where they hold. New functionality belongs in the
modular structure — see `docs/ddd-migration/core-modules-target-architecture-and-plan.md` for the migration
strategy.

## Three-project module layout

The refactored core is split across three projects with a linear, one-way dependency chain:

```
Core  ──►  Core.DataAccess  ──►  Core.Domain
```

| Project | Path | Responsibility |
|---|---|---|
| `Core.Domain` | `src/Modules/Core.Domain/` | Pure DDD aggregates, value objects, domain events, port interfaces (`IRepository`, `IUnitOfWork`). No EF, no HTTP, no async in invariants. |
| `Core.DataAccess` | `src/Modules/Core.DataAccess/` | EF DbContext wired directly to domain entities, write adapters (IRepository implementations), read queries (`IDataQuery`), read DTOs. |
| `Core` | `src/Modules/Core/` | Use-cases = Minimal API endpoints, request/response DTOs, response projections, output cache, composition (`AddCore`, `MapCoreEndpoints`). |

Reads and writes take different paths through it:

- **Write:** `Core` endpoint → `IRepository` (port in `Core.Domain`) → EF repo in `Core.DataAccess` →
  mutates aggregate → `IUnitOfWork.SaveChangesAsync()`
- **Read:** `Core` endpoint → `IDataQuery` (port in `Core.DataAccess`) → EF query object → read DTO →
  projected to Response

`ProjectReference` is transitive in SDK-style projects, so `Core` sees `Core.Domain` types through
`Core.DataAccess` without a direct reference.

## Feature structure

Each feature inside `Core` lives under `src/Modules/Core/Features/<Feature>/` and follows hexagonal
architecture:

| Layer | Purpose | Rules |
|---|---|---|
| `Model/` | Pure domain entities, ValueObjects, domain events | No EF Core, no HTTP, no async |
| `UseCases/` | Application services, DTOs, mappers | Orchestrates Model + Ports |
| `Ports/` | Interfaces for external dependencies (`IUserRepository`) | Interfaces only |
| `Adapters/` | Concrete implementations (EF Core repos, HTTP clients) | Implements Ports |
| `Api/` | Minimal API endpoint handlers | Request/Response DTOs live here |
| `Composition/` | DI registration (`AddCore()`) + endpoint mapping (`MapCoreEndpoints()`) | Entry point for module wiring |

## Composition pattern

Each module exposes `AddXxx(IServiceCollection)` and `MapXxxEndpoints(IEndpointRouteBuilder)` extension
methods in `Composition/`. `src/Core/API/Program.cs` calls them — a new feature registers its services and
maps its endpoints there.

## Endpoint conventions

- **Filename**: `<Verb><Resource>Endpoint.cs`, e.g. `GetProjectEndpoint.cs`
- **Handler**: `internal static async Task<IResult> HandleAsync(...)` — a top-level static method
- **Request**: `internal sealed record Request(...)`, defined inside the endpoint class and bound via
  `[AsParameters]`
- **Response**: `internal sealed record Response(...)`, defined inside the endpoint class; required whenever
  the endpoint returns data
- **Documentation**: a private `MapDocumentation()` extension member on `RouteHandlerBuilder` calling
  `.WithName()`, `.WithSummary()`, `.WithTags()` and `.Produces<>()`; add
  `.Accepts<Request>("application/json")` when the endpoint has a request body
- **Route mapping**: a C# 14 extension member on `IEndpointRouteBuilder` — not the older
  `this IEndpointRouteBuilder` extension method syntax

```csharp
internal static class GetProjectEndpoint
{
    internal sealed record Request([FromRoute] string ProjectShortName);
    internal sealed record Response(string ShortName, string Name);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        [FromServices] IRepository<Project, ProjectShortName> repository,
        CancellationToken ct) { ... }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetProjectEndpoint()
        {
            app.MapGet("projects/{projectShortName}", HandleAsync)
               .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("...").WithSummary("...").WithTags("...");
    }
}
```

## Domain model conventions

- **Base type**: aggregates extend `AggregateRoot` (from `Core.Domain.Primitives`); entities implement
  `IEntity`
- **Creation**: an `internal static Entity.Create(...)` factory — never `new` from outside the aggregate
- **IDs**: typed value objects such as `record UserId(Guid Value)`, never a raw `Guid`
- **Mutation**: private setters only; every state change goes through an aggregate method that enforces the
  invariants
- **Domain events**: `AddDomainEvent(new XxxCreatedEvent(...))` inside `Create` or a mutation method;
  `IUnitOfWork` dispatches them after `SaveChanges`
- **Sync invariants**: domain logic is synchronous — aggregate methods never `await`. When a mutation needs an
  entity that requires DB I/O to resolve or create (M:N), the *use-case* does the async resolution through a
  domain-owned port and passes the already-resolved entity into a synchronous aggregate method.
- **Many-to-many**: the use-case resolves-or-creates the shared aggregate through the generic repository port
  (`IRepository<ProjectTag, TagName>`: `FindAsync ?? AddAsync(ProjectTag.Create(...))`), then calls a sync
  aggregate method (`Project.AssignExistingTag(ProjectTag)`) that updates the private collection. The
  aggregate does not take the repository as a parameter.

## Error handling pattern

Expected failures are values, not exceptions. Domain errors use `Lukdrasil.Error`:

```csharp
// Core.Domain/User/UserErrors.cs
internal static class UserErrors
{
    internal static Error AdminRoleCannotBeChanged() =>
        new ErrorBuilder()
            .WithDescription("Admin role cannot be changed.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();
}
```

- One static `XxxErrors` class per aggregate, in `Core.Domain/<Aggregate>/`
- Aggregate methods return `Result<T>` or `Error` (from `Lukdrasil.Result`) on expected failure paths
- Exceptions are for programming errors only (`ArgumentNullException.ThrowIfNull` and friends)

## Workflow step payloads

A step record submitted to the WorkflowEngine is a **persisted contract**, not a parameter list. It is
serialised into `workflow.RequestSteps.InputData` (with `MessageType`) and re-materialised later, in another
DI scope and possibly in another process after a restart. The rules follow from that and are enforced
automatically — see
[`docs/architecture/adr/0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md`](adr/0101-workflow-step-payloads-carry-natural-keys-and-request-dtos.md).

- **Natural keys and request DTOs only.** `ProviderName`, not `Provider`;
  `ResolvedLocationRef(LocationName, Endpoint)`, not `(Location, LocationAssignment)`. No domain entity, no
  `AggregateRoot`, no EF-tracked object, no `ValueTuple`, and no interface other than `IReadOnlyList<>` /
  `IReadOnlyCollection<>`.
- **The handler resolves the aggregate itself**, from the payload's natural key, through a repository in its
  own scope and unit of work. An aggregate that is gone by then is a `PermanentException` with a stable error
  code, not a retry. Never operate on a graph that arrived from another scope: EF shadow keys die with the
  disposed `DbContext` while client-generated CLR keys survive, and `DbSet.Add` then cascades `Added` over the
  whole detached graph regardless of key.
- **The payload must survive a System.Text.Json round trip** with the runtime-type overloads
  (`Serialize(step, step.GetType())` / `Deserialize(raw, type)`). `ValueTuple` is the trap: it fails silently,
  serialising to `{}`.
- **Required members carry `[property: JsonRequired]`**, and no step constructor carries
  `[SetsRequiredMembers]` — it disables exactly that enforcement, and an old row would deserialise quietly
  into a default.
- **The CLR type name, namespace, assembly and `StateName` are part of the contract.** Adding an optional
  member is safe; renaming, moving or narrowing is a breaking change to rows already in the database and needs
  an operational plan for the in-flight requests.
- **Handlers are idempotent**, because delivery is at-least-once (retry, the retry scanner, startup recovery,
  a lost claim race). Assert idempotency by re-reading the **natural key**, never by matching an exception
  message or an index name — SQLite reports uniqueness by column names and SQL Server by index name, so a
  message match is dead code in production. A blanket `catch (DbUpdateException) { return success; }` is
  forbidden: it reports failed writes as completed steps.

Two automated guards hold this: `Tests/Modules/Core/Core.ArchitectureTests/WorkflowStepPayloadTests.cs`
(the shape rule) and `StepPayloadRoundTripTests.cs` (the round trip), plus the detached-aggregate guard in
`DatabaseContext.SaveChangesAsync`, which throws rather than letting a cascaded insert of a detached graph
reach the database.

## Testing

Tests are named `Should_<Expected>_When_<Context>()`. Test projects mirror the source layout:
`Tests/Modules/Core/` for the new modules, `Tests/Core/` for the legacy ones. There are **three** kinds:

| Kind | What it exercises | Fakes | Trait |
|---|---|---|---|
| **Unit** | domain logic directly on the entities | none | none |
| **Integration** | a use case with the Ports boundary mocked (`IUserRepository`, `IHeappeClient`, …), or a real external dependency | Ports, or EF-InMemory | `[Trait("Category", "Integration")]` |
| **In-process SQLite over the real EF model** | the behaviour of a *relational* provider that EF-InMemory fakes away | nothing outside the process | none — see below |

The third kind exists because EF-InMemory is not a database. It has no real `DbConnection`, no transactions,
no `ExecuteUpdateAsync`, no unique indexes and no FK ordering, so it silently accepts writes that SQL Server
rejects and silently grants leases no relational provider would grant. Anything whose subject is
change-tracker state, the SQL a save actually emits, a uniqueness collision, or a lease `UPDATE`'s affected
row count belongs here — for example
`Tests/Modules/Core/Core.UnitTests/Features/Resource/Workflow/Handlers/InitializeProjectResourceHandlerPersistenceTests.cs`
and `Tests/Modules/WorkflowEngine.UnitTests/Persistence/StepLeaseOwnershipTests.cs`.

Building one: open an in-memory SQLite connection, keep it open for the fixture's lifetime, and create the
schema from the **real** model with `Database.GenerateCreateScript()`. The generated script carries
SQL-Server-only syntax that SQLite rejects — `(max)` and the `SYSUTCDATETIME()` defaults used by 13
configurations — so rewrite those two in the script rather than stamping values in a seed helper: the INSERTs
under test are issued by production code's own `SaveChangesAsync`, which no test-side helper can reach.

Hand the connection **string** to `UseSqlite`, never the connection instance. A `DbContext` built from
options carrying a `SqliteConnection` instance shares that one object with every other context from the same
options, and `DbConnection` is thread-safe for no ADO.NET provider — a fixture driving a background worker
and the test thread at once then mutates one connection's command list from two threads (T-096). So name the
database and open it shared-cache, `Data Source=file:<unique-name>?mode=memory&cache=shared`, keep one
connection to it open for the fixture's lifetime, and pass only that string to `UseSqlite`. Each context
opens its own connection onto the same database; the kept-open one is what stops a shared in-memory database
from being dropped when its last connection closes.

One standing exception to the `Database.GenerateCreateScript()` recipe:
`Tests/Modules/WorkflowEngine.UnitTests/Helpers/SqliteWorkflowDatabase.cs` writes its DDL by hand, in its
`Schema` constant, because the `(max)`/`SYSUTCDATETIME()` rewrite does not cover what its model needs —
`rowversion` columns SQLite has no equivalent for, the `workflow` schema prefix that has to be dropped
because SQLite has no schemas, and `DateTimeOffset` columns stored as INTEGER by the order-preserving
converter the fixture installs through `ReplaceService<IModelCustomizer, ...>`.

**These tests carry no `[Trait("Category", "Integration")]`, deliberately.** In `Core.UnitTests` that trait
has one established meaning: the test needs an external SQL Server through `IntegrationDb` and *skips* when
`USERORG_TEST_DB` is unset (`Tests/Modules/Core/Core.UnitTests/Outbox/OutboxTransactionalIntegrationTests.cs`).
SQLite tests need nothing this process cannot start, must run in every default `dotnet test`, and are the only
cover for the relational behaviour described above — so any CI filter written as `Category != Integration`
would drop precisely the tests that catch this class of defect. The rule to apply going forward: the trait
means *"needs something this process cannot start"*, not *"touches a database"*.

> Known inconsistency, not fixed here: `Tests/Modules/WorkflowEngine.UnitTests/Persistence/StepLeaseOwnershipTests.cs`
> and its siblings tag in-process SQLite suites with the Integration trait even though they need no external
> database. Under the rule above those tags are wrong, but re-tagging pre-existing suites is a separate change
> with its own CI blast radius. The suites added by this change carry no trait.

Two limits of the SQLite harness, both real and both currently accepted:

- The real `IUnitOfWork` routes durable domain events into `OutboxDbContext`, whose options hardcode
  `UseSqlServer`, so a save that raises a durable event dies under SQLite with `near "SAVE": syntax error`.
  These suites therefore register a test-local `IUnitOfWork` that saves through `DbContext.SaveChangesAsync`,
  which means they do **not** exercise `ApplyConventions` or the outbox leg. Outbox atomicity keeps its own
  cover in `OutboxTransactionalIntegrationTests`. The hardcoded `UseSqlServer` is a genuine testability defect
  in production code and is tracked separately.
- SQLite is not SQL Server. It reports a uniqueness violation by column names where SQL Server reports an
  index name, which is why production code must never match on either (see *Workflow step payloads* above).

The solution runs on Microsoft.Testing.Platform (`global.json` sets `"runner": "Microsoft.Testing.Platform"`),
so a single test is selected with `--filter-query`, not the VSTest `--filter`:

```bash
dotnet test <path/to/Project.csproj> --no-build --list-tests
dotnet test <path/to/Project.csproj> --no-build --filter-query "/*/*/*/MethodName"
```

## Code style

`.editorconfig` and `Directory.Build.props` are authoritative — `EnforceCodeStyleInBuild=true` means the build
fails on a violation, so this section is a summary, not a second source of truth. File-scoped namespaces,
`using` before the namespace, 4-space indentation, CRLF in C# files, PascalCase types and methods, camelCase
locals, `_field` for private fields, `sealed` by default, nullable reference types on throughout, records for
immutable types and `readonly record struct` for small value objects. C# 14 language features (extension
members, collection expressions) are available and used.

### LINQ syntax in read queries

Method syntax is the default (ADR-0042). Query syntax is reserved for **multi-set joins over shadow FKs** in
`Core.DataAccess/Queries`, and the reason is scoping, not translation:

- `IDataQuery<T>` hands a composer a plain `Func<IQueryable<T>, …, IQueryable<TResult>>`, so both syntaxes
  build the **same expression tree** and translate to the same SQL. Neither form is more or less
  translatable than the other. `LinqSyntaxRewriteEquivalenceTests` pins this for the reads already converted.
- What query syntax actually buys is the compiler's **transparent identifiers**. In a chain that joins three
  or four sets and interleaves a `where` guard between the joins — the shape almost every scoped read has,
  because each hop needs its own `!IsDeleted` — every earlier range variable stays in scope for free. The
  same chain in method syntax needs a hand-written anonymous carrier per `.Join()` step to keep those
  variables reachable. It compiles to the same thing and reads considerably worse.
- **`join … equals` is not optional, in either syntax.** Over a shadow FK a nested correlated `.Any()`
  silently returns an empty set under the EF InMemory provider, so the join must stay an explicit join —
  `join … equals` or `.Join(…)`. That is a constraint on the *operator*, and it is the rule ADR-0042 and the
  F1/F5 plans are actually stating; it says nothing about which syntax spells it.
- A composer with **no join** — `where` / `orderby` / `select` over a single set, an `EXISTS` written as
  `otherSet.Any(…)` inside the predicate included — has no reason to be in query syntax. Write it in method
  syntax.

A **left join** is the most verbose construct to convert, but it does convert: `join … into g` + `from … in
g.DefaultIfEmpty()` becomes `GroupJoin(…).SelectMany(g => g.DefaultIfEmpty(), …)`. One catch — when the two
key expressions differ in nullability (`ProviderId?` vs `ProviderId`) generic inference picks no single `TKey`
and fails; casting the inner key (`(ProviderId?)EF.Property<ProviderId>(prov, "Id")`) fixes it. Both forms emit
byte-identical SQL; `ResourceDetailsQuery`'s paged read is the worked example. Converting one buys nothing on
its own, so leave existing left joins in query syntax unless you are already changing them.
