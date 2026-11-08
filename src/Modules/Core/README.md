# Core modules

Tři projekty tvoří jeden logický modul. Závislosti tečou lineárně:

```
Core  ──►  Core.DataAccess  ──►  Core.Domain
```

`Core.Domain` nezná EF ani HTTP. `Core.DataAccess` neví o endpointech. `Core` neví o databázi.

---

## Core.Domain — write model

**Co sem patří:** DDD entity a agregáty, value objekty, domain eventy, porty (`IRepository`, `IUnitOfWork`, `IDomainEventHandler`), primitiva (`AggregateRoot`, `IDataQuery`).

**Co sem nepatří:** EF, HTTP, async I/O v agregátech, read DTO, konfigurace aplikace.

```
Core.Domain/
  Project/      — agregát Project, value objekty, domain eventy
  Provider/     — agregát Provider (Location, ProjectResource, …)
  Request/      — agregát Request
  User/         — agregát User
  Configuration/
  Primitives/   — IRepository, IUnitOfWork, IDataQuery, AggregateRoot, …
  Shared/       — sdílené VO (Email, Description, …)
```

---

## Core.DataAccess — persistence + read side

**Co sem patří:** EF `DatabaseContext`, `IEntityTypeConfiguration` přímo nad DDD entitami, implementace repozitářů (`IRepository`), read query porty (`IUserInfoQuery`) a jejich read DTO (`UserDetails`, `UserExtendedDetails`, …), read query implementace (`UserDetailsQuery`), domain event infrastruktura.

**Co sem nepatří:** HTTP, endpointy, response DTO pro API.

```
Core.DataAccess/
  Configurations/   — EF konfigurace nad DDD entitami (jedna sada)
  Contexts/         — DatabaseContext + partial repozitáře
  Events/           — DomainEventChannel, DomainEventDispatcherService
  Migrations/       — EF migrace (kontext: DatabaseContext)
  Queries/
    Contracts/      — IUserInfoQuery + read DTO (UserDetails, …)
    DataQuery.cs    — generická IDataQuery<T> implementace
    UserDetailsQuery.cs
```

---

## Core — use-cases / API vrstva

**Co sem patří:** Minimal API endpointy, Request/Response DTO, mappery a projekce, output cache policies, composition (`AddCoreIntegration`, `MapV2Endpoints`).

**Co sem nepatří:** Přímý přístup do DB, EF, doménová logika.

```
Core/
  Features/
    Project/
      GetProject/   — GetProjectEndpoint, GetProjectResponse
    (další feature-slices…)
  Endpoints/
    UserInfo/       — GetUserInfo, GetExtendedUserInfo, projekce, loader
  Shared/
    OutputCache/    — CoreCachePolicy, CacheTags, IOutputCacheInvalidator, eviction handlers (viz README.md)
  EndpointsExtensions.cs   — MapV2Endpoints
  ServiceCollectionExtensions.cs — AddCoreIntegration
```

### Konvence pro nový endpoint

1. Složka `Features/<Resource>/<Verb><Resource>/`
2. Soubor `<Verb><Resource>Endpoint.cs` — `internal static class`, handler `HandleAsync`, mapování `extension(IEndpointRouteBuilder)`
3. Soubor `<Verb><Resource>Response.cs` — `internal sealed record`, nikdy nevracet agregát
4. Registrace v `EndpointsExtensions.cs` (`MapV2Endpoints`) a v `ServiceCollectionExtensions.cs` pokud je potřeba DI

---

## Datové cesty

**Write:** `Endpoint (Core)` → `IRepository port (Core.Domain)` → `DatabaseContext repo (Core.DataAccess)` → mutace agregátu → `IUnitOfWork.SaveChangesAsync`

**Read:** `Endpoint (Core)` → `IUserInfoQuery port (Core.DataAccess.Queries.Contracts)` → `UserDetailsQuery (Core.DataAccess)` → read DTO → projekce → Response
