# Architecture Guide — Team Onboarding

Praktický průvodce architekturou pro nové vývojáře. Odpovídá na otázky: "Kde je…?", "Jak přidám…?", "Jaký je naming?"

---

## Quick Reference — Cheat Sheet

| Otázka | Odpověď |
|-----------|---------|
| Kde je Core modul? | `src/Modules/Core/` |
| Kde je User entita? | `src/Modules/Core/Features/Users/Model/User.cs` |
| Kde je User repository? | `src/Modules/Core/Features/Users/Adapters/UserRepository.cs` |
| Kde je Create User endpoint? | `src/Modules/Core/Features/Users/Api/CreateUserEndpoint.cs` |
| Kde je DI registrace Core modulu? | `src/Modules/Core/Composition/CoreModule.cs` |
| Jak registruji nový modul? | Vytvoř `Composition/AddMyModule()` v modulu, zavolej v `API/Program.cs` |
| Jaké je pojmenování endpointu? | `Create<Resource>Endpoint.cs`, `Get<Resource>Endpoint.cs`, `Update<Resource>Endpoint.cs`, atd. |
| Kde je Request/Response DTO? | V samotném endpointu: `sealed record Request {...}`, `sealed record Response(...)` |
| Co je Port? | Rozhraní/kontrakt definovaný modulem (např. `IUserRepository`). Může být "driving" (inbound — volají ho služby zvenčí) nebo "driven" (outbound — používá ho UseCases pro přístup k externím závislostem). |
| Co je Adapter? | Konkrétní implementace portu — concrete bridge mezi modulem a externím světem (DB, HTTP, cache). Adapter implementuje port definovaný modulem a mapuje doménové objekty na infra modely (např. `UserRepository` implementuje `IUserRepository`). |

---

## Directory Structure — Navigace

### Spustitelné projekty (Entry Points)

```
src/
├─ API/                              # Spustitelný REST API server
│  ├─ Program.cs                     # Zaregistruje moduly, mapuje endpointy
│  └─ appsettings.json
└─ Gateway/                          # Spustitelný reverse proxy (YARP)
   ├─ Program.cs                     # Orchestrator pro request routing
   └─ appsettings.json
```

**Gateway vs API:**
- **Gateway** = vstupní vrátka, reverse proxy, token introspection, routing na dalších služby
- **API** = hlavní REST business logika server

### Moduly (Feature Libraries)

```
src/Modules/
├─ Core/                             # Základní modul — persistence, core features
│  ├─ Core.csproj
│  ├─ Persistence/                   # Centrální EF Core setup
│  │  ├─ Db/UserOrgDbContext.cs
│  │  ├─ Entities/                   # EF entities (interní)
│  │  └─ EfCoreConfigurations/
│  ├─ Features/
│  │  ├─ Users/
│  │  │  ├─ Model/                   # Pure domain: User.cs
│  │  │  ├─ UseCases/                # App logic: CreateUserService.cs, DTOs, mappers
│  │  │  ├─ Ports/                   # Interfaces: IUserRepository.cs
│  │  │  ├─ Adapters/                # DB: UserRepository.cs
│  │  │  ├─ Api/                     # Endpoints: CreateUserEndpoint.cs
│  │  │  └─ Tests/                   # Unit+Integration: CreateUserEndpointTests.cs
│  │  └─ [Roles, Projects, ...]      # Další features
│  └─ Composition/                   # DI + Endpoint mapping
│     ├─ CoreModule.cs               # AddCore() — zaregistruj services
│     └─ CoreEndpoints.cs            # MapCoreEndpoints() — mapuj endpoints
└─ Integration/
   └─ Heappe/                        # HEAppE adapter
      ├─ Model/                      # HPC job domain models
      ├─ UseCases/                   # Orchestration
      ├─ Ports/                      # IHeappeClient.cs
      ├─ Adapters/                   # HeappeHttpClient.cs
      ├─ Api/                        # Endpoints (pokud nějaké)
      └─ Composition/                # AddHeappe(), MapHeappeEndpoints()
```

### Shared Libraries (Cross-Cutting)

```
src/Shared/Libs/
├─ RoleManagement/                   # Permission/Role logic (byla Forge.Core)
│  ├─ Model/
│  ├─ UseCases/
│  ├─ Ports/
│  ├─ Adapters/
│  └─ Composition/
└─ Kernel/                          # Shared utilities, DTOs, helpers
   ├─ Result<T>
   ├─ Common DTOs
   └─ Extensions
```

---

## Layers Explained — Co kde žije

### Model/ — Pure Domain Logic

**Účel**: Datové struktury + business pravidla entit (bez infrastruktury).

**Co obsahuje**:
- **Entities** — Domain model s chováním (Rich Domain Model!)
  ```csharp
  public sealed class User
  {
      public UserId Id { get; private set; }
      private string _email;
      
      public static User Create(string email) { ... }
      public void ChangeEmail(string newEmail) { ... }  // ← Entita hlídá invarianty
  }
  ```
- **ValueObjects** — neměnné dataholders
  ```csharp
  public record UserId(Guid Value);
  ```
- **Domain Events** — co se stalo
  ```csharp
  public record UserCreated(UserId Id, string Email);
  ```
- **Exceptions** — domain-specifické výjimky
  ```csharp
  public class InvalidEmailException : Exception { ... }
  ```

**Co NESMÍ obsahovat**:
- ❌ EF Core `DbSet`, `DbContext`
- ❌ HTTP logika (HttpClient, response codes)
- ❌ IO operace (file system, network)
- ❌ Async `Task` — domain logika je synchronní

**Testing**: Testuj Model přímo, bez mockování. Entita je čistá, deterministic.

---

### UseCases/ — Application Orchestration

**Účel**: Application services, orchestrace business procesů, DTO mapování.

**Co obsahuje**:
- **Services** — orchestrace (e.g., `CreateUserService.cs`)
  ```csharp
  public sealed class CreateUserService
  {
      private readonly IUserRepository _repo;
      
      public async Task<CreateUserResponse> Handle(CreateUserRequest request)
      {
          var user = User.Create(request.Email);  // ← Model
          await _repo.Add(user);
          return user.ToResponse();  // ← DTOs
      }
  }
  ```
- **DTOs** — Request/Response records (interní pro Use Case)
  ```csharp
  public sealed record CreateUserRequest(string Email);
  public sealed record CreateUserResponse(Guid Id, string Email);
  ```
- **Mappers** — extension classes pro DTO/Entity transformace
  ```csharp
  internal static class UserExtensions
  {
      internal static CreateUserResponse ToResponse(this User user) 
          => new(user.Id.Value, user.Email);
  }
  ```

**Co NESMÍ obsahovat**:
- ❌ EF Core entities (jen Model entities)
- ❌ HTTP-specifické chování

**Testing**: Mockuj Ports (IUserRepository), testuj orchestraci.

---

### Ports/ — Contracts for External Dependencies

**Účel**: Rozhraní (interfaces) které definují "jak chceme mluvit s vnějším světem".

**Co obsahuje**:
- **Repository Interfaces** — "jak se ptám na data"
  ```csharp
  public interface IUserRepository
  {
      Task Add(User user);
      Task<User?> GetById(UserId id);
      Task Update(User user);
  }
  ```
- **Service Interfaces** — "jak se ptám na externích službu"
  ```csharp
  public interface IHeappeClient
  {
      Task<JobSubmissionResult> SubmitJob(HpcJobRequest request);
  }
  ```

**Poznámka — inbound vs outbound porty**:
- **Driving (inbound) ports**: rozhraní, která modul vystavuje a která volají vyšší vrstvy (např. API nebo jiný modul). Příklad: `IUserService` může být inbound port, který volá API.
- **Driven (outbound) ports**: rozhraní, která modul používá pro komunikaci s externím světem (DB, HTTP). Příklad: `IUserRepository`.

**Co NESMÍ obsahovat**:
- ❌ Implementace (jen signatures)

**Testing**: Mockuj tyto interfacy v testech.

---

### Adapters/ — Concrete Implementations

**Účel**: Praktická implementace Portů — jak se mluví s DB, HTTP API, atd.

**Co obsahuje**:
- **Repositories** — EF Core implementace
  ```csharp
  public sealed class UserRepository : IUserRepository
  {
      private readonly UserOrgDbContext _db;
      
      public async Task Add(User user)
      {
          // ← Mapuj doménový User na EF entity
          var efEntity = new UserEntity { ... };
          _db.Users.Add(efEntity);
          await _db.SaveChangesAsync();
      }
  }
  ```
- **HTTP Clients** — externích API calls
  ```csharp
  public sealed class HeappeHttpClient : IHeappeClient
  {
      private readonly HttpClient _http;
      public async Task<JobSubmissionResult> SubmitJob(...) { ... }
  }
  ```

**Testing**: Tady jsou závislosty na externích systémech → integration testy + mocking.

**Doplňující vysvětlení**:
- Adaptery implementují porty, které modul definuje. To znamená, že nejprve definujete kontrakt v `Ports/` (např. `IUserRepository`), a pak vytvoříte adapter (např. `UserRepository`) v `Adapters/`, který tento kontrakt naplní.
- Adapter může také vystavovat "primary adapter" chování (např. HTTP controller) — v našem patternu ale HTTP transport patří do `Api/` vrstvy; Adapters většinou řeší "driven" scénáře (DB, externí API). 
- Adapter by měl mapovat mezi doménovým modelem (`Model/`) a infra/EF entitami (`Persistence/Entities`) a minimalizovat leaks doménových typů do infra vrstev.

---

### Api/ — HTTP Transport Layer

**Účel**: Minimal API endpointy — request/response příjmutí a vrácení.

**Co obsahuje**:
- **Endpoints** — Minimal API handlery
  ```csharp
  public sealed class CreateUserEndpoint
  {
      public sealed record Request(string Email);
      public sealed record Response(Guid Id, string Email);
      
      internal static IEndpointRouteBuilder MapCreateUserEndpoint(
          this IEndpointRouteBuilder app)
      {
          app.MapPost("/users", HandleAsync)
              .WithName("CreateUser")
              .WithOpenApi();
          return app;
      }
      
      private static async Task<IResult> HandleAsync(
          Request request,
          [FromServices] CreateUserService service,
          CancellationToken ct)
      {
          var result = await service.Handle(new(request.Email));
          return Results.Created($"/users/{result.Id}", result);
      }
  }
  ```

**Konvence**:
- **Filename** = `<Verb><Resource>Endpoint.cs` (e.g., `CreateUserEndpoint.cs`, `GetUserEndpoint.cs`)
- **Mapping method** = `Map<Name>Endpoint()`
- **Handler** = `HandleAsync()`
- **Request/Response** = `sealed record` v samotném endpointu

**Testing**: Integrační testy → `WebApplicationFactory` nebo mock `HttpClient`.

---

## Feature Ownership — "Kdo je za co?"

```
Core Module
├─ Users/          → User management, authentication entities
├─ Roles/          → Role definitions
├─ Projects/       → Project management
└─ Permissions/    → Permission definitions

RoleManagement (Shared/Libs)
└─ Role-based permission enforcement (cross-cutting)

Integration/Heappe
└─ HPC job submission + monitoring
```

Každý feature má svou složku; všechny layers (Model/UseCases/Ports/Adapters/Api) jsou v ní.

---

## Composition Pattern — Jak se věci zaregistrují

### 1. Modul má AddModule() extension

```csharp
// src/Modules/Core/Composition/CoreModule.cs
public static class CoreModule
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<CreateUserService>();
        // ... další registrace
        return services;
    }
}
```

### 2. Modul má MapModuleEndpoints() extension

```csharp
// src/Modules/Core/Composition/CoreEndpoints.cs
public static class CoreEndpoints
{
    public static IEndpointRouteBuilder MapCoreEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapCreateUserEndpoint();
        app.MapGetUserEndpoint();
        // ... další endpointy
        return app;
    }
}
```

### 3. Entry Point (API/Gateway) zaregistruje všechny moduly

```csharp
// src/API/Program.cs
var services = builder.Services;

// Zaregistruj moduly
services
    .AddCore()                      // ← Core module DI
    .AddRoleManagement()            // ← Shared RoleManagement
    .AddHeappe();                   // ← Integration/Heappe

// Mapuj endpointy
var app = builder.Build();
app
    .MapCoreEndpoints()             // ← Core endpoints
    .MapRoleManagementEndpoints()   // ← Role/Permission endpoints
    .MapHeappeEndpoints();          // ← HEAppE endpoints
```

---

## Common Patterns & Examples

### Jak přidám nový User feature (e.g., "Delete User")?

**Krok 1**: Přidej delete metodu na User entitu
```csharp
// src/Modules/Core/Features/Users/Model/User.cs
public sealed class User
{
    public void Delete() 
    {
        if (IsDeleted) throw new DomainException("Already deleted");
        IsDeleted = true;
    }
    
    public bool IsDeleted { get; private set; }
}
```

**Krok 2**: Vytvoř DeleteUserService
```csharp
// src/Modules/Core/Features/Users/UseCases/DeleteUserService.cs
public sealed class DeleteUserService
{
    private readonly IUserRepository _repo;
    
    public async Task Handle(Guid userId)
    {
        var user = await _repo.GetById(new UserId(userId));
        if (user is null) throw new NotFoundException("User not found");
        user.Delete();
        await _repo.Update(user);
    }
}
```

**Krok 3**: Vytvoř DeleteUserEndpoint
```csharp
// src/Modules/Core/Features/Users/Api/DeleteUserEndpoint.cs
public sealed class DeleteUserEndpoint
{
    internal static IEndpointRouteBuilder MapDeleteUserEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapDelete("/users/{id}", HandleAsync);
        return app;
    }
    
    private static async Task<IResult> HandleAsync(
        Guid id,
        [FromServices] DeleteUserService service)
    {
        await service.Handle(id);
        return Results.NoContent();
    }
}
```

**Krok 4**: Zaregistruj endpoint v Composition
```csharp
// src/Modules/Core/Composition/CoreEndpoints.cs
public static class CoreEndpoints
{
    public static IEndpointRouteBuilder MapCoreEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapCreateUserEndpoint();
        app.MapGetUserEndpoint();
        app.MapDeleteUserEndpoint();  // ← Nový!
        return app;
    }
}
```

**Krok 5**: Zaregistruj service (pokud není už)
```csharp
// src/Modules/Core/Composition/CoreModule.cs
services.AddScoped<DeleteUserService>();
```

---

### Jak přidám nový modul (e.g., "Reports")?

**Struktura** (kopírování šablony):
```
src/Modules/Reports/
├─ Reports.csproj
├─ Composition/
│  ├─ ReportsModule.cs           # AddReports()
│  └─ ReportsEndpoints.cs        # MapReportsEndpoints()
├─ Features/
│  └─ GenerateReport/
│     ├─ Model/
│     ├─ UseCases/
│     ├─ Ports/
│     ├─ Adapters/
│     ├─ Api/
│     └─ Tests/
├─ Persistence/                  # Pokud má svou DB
└─ Tests/
```

**Registrace v API**:
```csharp
// src/API/Program.cs
services.AddReports();
app.MapReportsEndpoints();
```

---

## Code Conventions — Návyky

| Konvence | Příklad |
|----------|---------|
| Namespace | `file-scoped` — `namespace UserOrg.Core.Features.Users.Model;` (bez closing brace) |
| Entity | `sealed class User { private set; }` — property nemá public setter |
| Factory | `User.Create()` — statická metoda pro tvorbu, ne `new` |
| ID Types | `record UserId(Guid Value)` — value object pro ID |
| Exceptions | Domain-specific: `InvalidEmailException`, ne generic |
| DTO Names | `CreateUserRequest`, `CreateUserResponse` — v samotném endpointu souboru |
| Extension Classes | `UserExtensions.ToResponse()` — mapování v `<Entity>Extensions.cs` |
| Interface Naming | `IUserRepository`, `IHeappeClient` — prefix `I` vždy |
| Async | Vždy `async Task`, nikdy `Task.Result` |
| Logging | `ILogger<T>` v constructoru — struktuované logging s correlation IDs |

---

## Testing Strategy

### Unit Tests (v Model/)
- Testuj domain logiku přímo na entitě bez mockování
- `User.Create()`, `user.ChangeEmail()`, exception throwing
- **File**: `<Feature>.Model.Tests.cs`

### Integration Tests (v Adapters + Api/)
- Testuj repository → DB komunikaci
- Testuj endpoint → request/response transformaci
- Mockuj externích služby (IHeappeClient atd.)
- **File**: `<Feature>IntegrationTests.cs`

### Example:
```csharp
[Category("Integration")]
public sealed class CreateUserEndpointTests
{
    [Fact]
    public async Task Should_CreateUser_When_ValidRequest()
    {
        // Arrange
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var request = new { email = "test@example.com" };
        
        // Act
        var response = await client.PostAsJsonAsync("/users", request);
        
        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }
}
```

---

## Glossary

| Term | Meaning |
|------|---------|
| **Model/** | Pure domain entities, ValueObjects, no infrastructure |
| **UseCases/** | Application services, orchestration, DTOs |
| **Ports/** | Interfaces defining external dependencies |
| **Adapters/** | Concrete implementations (repos, HTTP clients) |
| **Api/** | Minimal API endpoints |
| **Composition** | DI registration + endpoint mapping |
| **Rich Domain Model** | Entity with behavior (not just data) |
| **Port/Adapter Pattern** | Hexagonal architecture — ports=interfaces, adapters=impl |
| **DTO** | Data Transfer Object — for API request/response |
| **Mapper** | Extension class transforming between entities/DTOs |
| **Feature Slice** | One folder containing all layers for a feature |
| **Scoped** | Lifetime: one instance per HTTP request |
| **Sealed** | Type cannot be inherited — safer by default |

---

## Common Questions

**Q: Proč Model nesmí mít async Task?**  
A: Domain logika je čistá, bez IO. Async patří do Use Cases/Adapters.

**Q: Když už mám Response DTO v endpointu, proč Extension Mapper?**  
A: Extension metody (`ToResponse()`) jsou samostatné, reusetabilní a jasně signalizují transformaci.

**Q: Mohu smazat DbContext?**  
A: Nikdy! DbContext je centrální v `Persistence/Db/UserOrgDbContext.cs` — všechny entity tam jsou konfigury.

**Q: Jak testuju repository bez reálné DB?**  
A: Mockuj `IUserRepository` interface. V integration testech použi in-memory DB nebo Docker SQL Server.

**Q: Proč "sealed"?**  
A: Inheritance je zdrojem zmatení. Sealed třídy jsou bezpečnější a jasněji signalizují: "to se nemá dědičit".

---

## Resources

- [CORE_ARCHITECTURE_COMBINED.md](../CORE_ARCHITECTURE_COMBINED.md) — Detailní architektura a refactor plán
- [REFACTORING_PLAN.md](./REFACTORING_PLAN.md) — Step-by-step refactor instructionss pro juniorů
- [minimal-api-csharp SKILL](../../.github/skills/minimal-api-csharp/SKILL.md) — Endpoint creation convention
- [csharp.instructions.md](../../.github/instructions/csharp.instructions.md) — C# code style
