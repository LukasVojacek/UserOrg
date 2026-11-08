# Osa A / P1 — implementační plán (write-model purity)

> **Companion k** [core-modules-target-architecture-and-plan.md §8](./core-modules-target-architecture-and-plan.md) a [ADR-0008 addendum](../architecture/adr/0008-core-owns-project-role-writes.md).
> **Cíl P1:** `Project` agregát bez `async` a bez `I*Handler` parametrů; role-invarianty unit-testovatelné přímo na entitě.
> **Datum:** 2026-06-08.

Plán je rozdělený do **commit-sized kroků**; po každém je build zelený a chování zachované. Pořadí je závazné (commit 2 závisí na 1).

---

## Commit 1 — EF model: `_memberRoles` jako načtená kolekce (žádná změna chování)

Přidat persistenci, ještě nezapojit do mutací. Write cesta dál běží přes handler — `_memberRoles` se zatím jen hydratuje a čte.

**1.1 `UserPermission` (Core.Domain/User/UserPermission.cs)**
- Přidat `internal User User { get; private set; } = default!;` navigaci (vzor `AssignedUserToProject`). V namespace `Core.Domain.User` je typ `User` přímo dostupný.
- Factory `CreateProjectRole` + mutátory (`ChangeRole`/`Reactivate`/`SoftDelete`) se přidávají až v **Commitu 2** (kde se používají) — v Commitu 1 by byly bez volajícího. Tvar pro Commit 2:
  ```csharp
  internal static UserPermission CreateProjectRole(User user, ProjectRoleId roleId) =>
      new() { User = user, SystemRoleId = roleId };   // UserId/ProjectId dořeší EF z navigace + parent kolekce
  internal void ChangeRole(ProjectRoleId roleId) => SystemRoleId = roleId;
  internal void Reactivate(ProjectRoleId roleId) { IsDeleted = false; SystemRoleId = roleId; }
  internal void SoftDelete() => IsDeleted = true;
  ```

**1.2 `UserPermissionConfiguration.cs`**
- Pojmenovat globální filtr (EF Core 10 named query filter):
  ```csharp
  builder.HasQueryFilter("SystemRoleOnly", up => up.ProjectId == null);
  ```

**1.3a `ProjectConfiguration.cs` — nová relace Project→`_memberRoles` (FK `ProjectId`, bez inverzní navigace)**
```csharp
builder.HasMany<UserPermission>("_memberRoles")
    .WithOne()                            // žádná inverzní nav na Project straně
    .HasForeignKey(up => up.ProjectId)    // mapuje existující FK_UserPermission_Project_ProjectId
    .IsRequired(false)                    // ProjectId je nullable (system role sem nespadá)
    .OnDelete(DeleteBehavior.NoAction);
```

**1.3b `UserConfiguration.cs` — `User` navigace patří k relaci přes `UserId`, ne k Project relaci**
Sdílená `User` navigace je inverzní stranou existující User↔UserPermission relace (FK `UserId`). Změnit `_systemPermissions` z `.WithOne()` na `.WithOne(up => up.User)`:
```csharp
builder.HasMany<UserPermission>("_systemPermissions")
    .WithOne(up => up.User)               // dříve WithOne()
    .HasForeignKey("UserId") ...
```
> ⚠️ `UserPermission` tak má **dvě relace** (User přes `UserId` — vlastní `User` nav; Project přes `ProjectId` — bez nav) a **jednu** `User` navigaci. Schéma už FK `FK_UserPermission_Project_ProjectId` obsahuje, takže relace jen mapuje existující constraint.

**1.4 `Project.cs` — backing field**
```csharp
private readonly ICollection<UserPermission> _memberRoles = [];
```
> `ActiveMemberIdpIds` projekce (pro DeleteProject eviction) se přidává až v **Commitu 2** — v Commitu 1 by neměla volajícího. `_memberRoles` samotné je EF backing field; unused-field warning nenastane (build ověřeno).

**1.5 `DatabaseContext.ProjectRepository.cs` — hydratace**
```csharp
private IQueryable<Project> _projectincludes => Projects
    .IgnoreQueryFilters(["SystemRoleOnly"])   // jen UserPermission filtr; Project soft-delete zůstává
    .AsSplitQuery()
    .Include("_projectDomainType")
    .Include("_projectTags")
    .Include("_assignedResources.ProjectResource")
    .Include("_assignedUsers.User")
    .Include("_memberRoles.User")
    .AsSplitQuery();
```

**Verify (skutečný gate — migrace jsou zmražené):**
- `dotnet build src/Modules/Core.DataAccess` zelený (kompiluje named-filter API i navigace).
- `dotnet ef dbcontext info --project src/Modules/Core.DataAccess` projde → **EF model se postaví** (relace `_memberRoles`, sdílená `User` nav a pojmenovaný filtr jsou validní vůči existujícímu schématu).
- `dotnet test Core.UnitTests` beze změny oproti baseline (pozn.: 8 `*DatabaseAccessTests` selhává **pre-existing** — DI gap `IRoleService`/`ILogger<DatabaseContext>`, nesouvisí s touto změnou).
- ⚠️ **NE** `dotnet ef migrations list/add` jako gate: snapshot je v legacy namespace `UserOrganizationManagement.Domain.Entities` (zmražený), takže generování migrace z nového modelu nedává smysl. Schéma už `FK_UserPermission_Project_ProjectId` + index na `ProjectId` obsahuje (ověřeno ve snapshotu).

---

## Commit 2 — Role-mutace synchronně; smazat `IProjectRoleHandler`

Jádro P1. Přepíše 3 agregátní metody na sync nad `_memberRoles`, upraví **8 volajících sites** + testy, smaže handler. (Realizováno.)

**2.1 `Project.cs` — 3 metody sync (match referencí `r.User == target`, jako membership) + helper**
- `AssignUserWithRole(User target, UserId assignedBy, ProjectRoleId roleId)` — `AssignUser` (existující sync) + `UpsertMemberRole`.
- `UnassignUserWithRole(User target, UserId unassignedBy, ProjectRoleId ownerRoleId)` — `EnsureNotReadOnly`; owner-check nad `_memberRoles` (`r.User==target && !IsDeleted && SystemRoleId==ownerRoleId` → `OwnerCannotBeUnassigned`); membership `UnassignUser`; `role.SoftDelete()`.
  > Kolize jmen vyřešena distinktním názvem: veřejné `UnassignUserWithRole` (3 args) vedle existujícího membership `UnassignUser(User, UserId)` (2 args) — různé názvy, žádný matoucí overload.
- `ChangeUserRole(User target, ProjectRoleAssignment assignment)` — `EnsureNotReadOnly`; hierarchy check (čistý); single-owner nad `_memberRoles`; `UpsertMemberRole`.
- `private void UpsertMemberRole(User target, ProjectRoleId roleId)` — create / `Reactivate` / `ChangeRole` nad `_memberRoles` (zrcadlí bývalý handler upsert).
- Test-affordance: `internal ProjectRoleId? ActiveRoleOf(User user)` (Core.Domain → Core.UnitTests přes IVT) — aby unit testy ověřily hodnotu role bez mocku.

**2.2 Smazat persistenci handleru**
- Smazat `Core.Domain/Project/IProjectRoleHandler.cs`.
- Smazat `Core.DataAccess/Contexts/DatabaseContext.ProjectRoleHandler.cs`.
- Odebrat DI registraci (`grep IProjectRoleHandler` v composition — `AddScoped<IProjectRoleHandler,…>`).

**2.3 Upravit 8 volajících sites** (odebrat `IProjectRoleHandler` injekci/param, zahodit `await`/handler argument):
- [AssignProjectUserEndpoint.cs](../../src/Modules/Core/Features/Project/AssignProjectUser/AssignProjectUserEndpoint.cs), [UnassignProjectUserEndpoint.cs](../../src/Modules/Core/Features/Project/UnassignProjectUser/UnassignProjectUserEndpoint.cs), [ChangeProjectUserRoleEndpoint.cs](../../src/Modules/Core/Features/Project/ChangeProjectUserRole/ChangeProjectUserRoleEndpoint.cs), [CreateProjectEndpoint.cs](../../src/Modules/Core/Features/Project/CreateProject/CreateProjectEndpoint.cs).
- [SelfOnboardingAccept.cs](../../src/Modules/Core/Features/Request/SelfOnboardingAccept.cs) (sdílený helper) — drop `IProjectRoleHandler` param. **A jeho dva volající** předávající handler: [AcceptRequestEndpoint.cs](../../src/Modules/Core/Features/Request/AcceptRequest/AcceptRequestEndpoint.cs), [HandleActionLinkEndpoint.cs](../../src/Modules/Core/Features/Request/HandleActionLink/HandleActionLinkEndpoint.cs) (doc uváděl 5 — tyto dva skrze helper přibyly).
- DeleteProject — viz 2.4.

**2.5 Přepsat testy** (8 souborů — staré symboly nezkompilují):
- `ProjectAggregateTests.cs`: smazat `FakeRoleHandler`; role testy → sync, ověření přes `Users` / `ActiveRoleOf` (žádné mocky).
- 7 endpoint testů: odebrat `Mock<IProjectRoleHandler>` + argument. Stavové testy (Unassign owner-check, ChangeRole second-owner, Delete eviction) nastavit stav přes `AssignUserWithRole` na agregátu místo handler-mocku.

**2.4 DeleteProject — cascade bez handleru** ([DeleteProjectEndpoint.cs](../../src/Modules/Core/Features/Project/DeleteProject/DeleteProjectEndpoint.cs))
- `Project.Delete()` rozšířit: soft-deletovat všechny aktivní `_memberRoles` (`foreach … SoftDelete()`) navíc k `IsDeleted=true` + eventu.
- Endpoint: odebrat `IProjectRoleHandler` + `SoftDeleteAllRolesAsync`; dotčené IdpId vzít z `project.ActiveMemberIdpIds` **zachycené před** `Delete()` (po Delete jsou členové stále v kolekci, jen soft-deleted — ale bezpečnější je snapshot před).

**Verify (realizováno):**
- `Project.cs` nemá žádnou `async` metodu role-cesty ani `I*Handler` parametr; `IProjectRoleHandler` + impl smazány, DI registrace odebrána.
- `dotnet build src/Modules/Core` zelený (0 errors); `dotnet ef dbcontext info` model OK.
- `dotnet test Core.UnitTests`: **241 passed / 8 failed — identicky s baseline** (těch 8 jsou pre-existing `*DatabaseAccessTests` DI selhání). Role-invarianty testovány přímo na entitě bez mocků.
- ⏳ E2E (Gateway round-trip): assign → change role → unassign → delete — ověřit při integračním běhu (chování má zůstat 204 / cache eviction / cascade).

---

## Commit 3 — Tag create-and-assign bez aggregate handleru

`AssignTagAsync` je jediná tag metoda s handlerem v agregátu. `AssignExistingTag(ProjectTag)` (sync) už existuje a běží.

- Smazat `Project.AssignTagAsync(...)` z [Project.cs:334](../../src/Modules/Core.Domain/Project/Project.cs#L334).
- [CreateProjectTagEndpoint.cs:47](../../src/Modules/Core/Features/Project/CreateProjectTag/CreateProjectTagEndpoint.cs#L47): resolve-or-create udělat **v endpointu** přes existující port, pak `project.AssignExistingTag(tag)`:
  ```csharp
  if (project.HasTag(tagName)) return Results.NoContent();        // idempotence (dnes uvnitř AssignTagAsync)
  var tag = await tagCatalogue.EnsureTagExistsAsync(tagName, actor.UserName);
  if (project.AssignExistingTag(tag) is { IsSuccess: false } assign) return assign.Error.ToProblem();
  ```
- `IProjectTagHandler` **NEmazat** — `EnsureTagExistsAsync`/`FindTagAsync` jsou legitimní **use-case katalogové operace** (čtení/zápis katalogu Tagů), ne doménové porty v agregátu. Zůstávají jako use-case dependency injektovaná do endpointů. (Volitelně přejmenovat na `IProjectTagCatalogue` pro jasnost — drobné, mimo P1.)

**Verify:** `AssignTagAsync` zmizí z `Project`; create-and-assign endpoint vrací `204` (vč. idempotence při existujícím tagu); assign-existing endpoint beze změny.

---

## Commit 4 — Smazat mrtvý resource kód

`AddResourceAsync`/`RemoveResourceAsync` + `IProjectResourceHandler` mají **0 volání** v `Core` (resource-write odložen, ADR-0009).

- Smazat `Project.AddResourceAsync`/`RemoveResourceAsync`.
- Smazat `Core.Domain/Project/IProjectResourceHandler.cs`. **Impl ani DI registrace neexistovaly** — interface byl jen definovaný, nikdy nezapojený. (`_assignedResources` zůstává, čte ho `Resources` projekce.)

**Verify (realizováno):** build zelený; grep nenajde referenci na smazané symboly; testy beze změny (247/8/239).

---

## Po P1: stav agregátu — ✅ hotovo

`Project` nemá žádnou `async` metodu ani parametr typu `I*Handler` (ověřeno grepem — jediný zásah je historická zmínka v doc-komentáři). Zbylé porty (`IProjectDirectory`, `IProjectDomainTypeLookup`) jsou synchronní lookupy VO volané use-casy **před** mutací, ne uvnitř invariant — cílový tvar (D-2). Commity: `76762896` (P1.1) · `dcfbd44f` (P1.2-3) · `3af846f6` (P1.4) · P1.5 tímto.

---

## P2 / P3 — mechanický zbytek (zkráceně)

Detail v [§8 plánu](./core-modules-target-architecture-and-plan.md). Commit-sized:

- **C5 (P2.1):** `AddCoreIntegration`→`AddCore`, `MapV2Endpoints`→`MapCoreEndpoints` (route „v2" zůstává), přesun do `Core/Composition/`. Upravit `src/Core/API/Program.cs`.
- **C6 (P2.2):** event-handlery assembly-scanem v `AddCoreDataAccess` (D8); cache-eviction handlery nechat v `AddCore`.
- **C7 (P2.3):** viditelnost `internal` všude kromě composition + Response DTO.
- **C8 (P3.1):** ✅ decoupling byl hotový dřív; dokončeno přejmenováním na `WellKnownRoles` + přesunem do `Core.Domain/User/` (`4f120489`).
- **C9 (P3.2):** ✅ cache policies (P0-C) i EF include-set (P1.1) hotové už dřív; env-guard `EnableSensitiveDataLogging` doplněn (`f7c1983d`).
- **C10 (P3.3):** ✅ golden řez + checklist → [golden-slice.md](./golden-slice.md).

> **P3 hotovo (2026-06-09).** Při plánování se ukázalo, že P0–P2 reorgy už splnily P3.1 (decoupling), P3.2a (cache co-location) a P3.2b (include-set); skutečně zbývaly jen env-guard, rename a golden-slice doc.

---

## Rizika / poznámky

- **EF Core 10 named query filters** (`HasQueryFilter("key", …)` + `IgnoreQueryFilters(["key"])`) jsou klíč pro selektivní obcházení `SystemRoleOnly` bez vypnutí Project soft-delete filtru. Pokud by se ukázalo, že nejsou k dispozici/spolehlivé, fallback: zrušit globální filtr na `UserPermission` a scope-ovat `_systemPermissions` přes oddělenou konfiguraci — invazivnější, proto až jako záloha.
- **Dvojí write cesta v commitu 1→2:** mezi commity 1 a 2 existuje handler i `_memberRoles` současně. Commit 1 `_memberRoles` jen čte/hydratuje (nemutuje), takže ke konfliktu trackování nedochází; přesto commit 2 nasadit hned po 1, ať dual-stack okno netrvá.
- **Snapshot `ActiveMemberIdpIds`:** v DeleteProject zachytit **před** `Delete()`.
