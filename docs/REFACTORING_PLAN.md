# Refactoring Plan — Junior Developer Guide

Detailní, fázový plán refaktoringu z monolitické Core na feature-sliced hexagonální architekturu. **Určeno pro juniory** — konkrétní kroky, příkazy, tipy na safety a checklesty.

---

## 📋 Plan Metadata (CLI-friendly)

```yaml
plan_id: core-refactoring-v1
total_phases: 6
estimated_duration_hours: 40
risk_level: medium
parallelizable: true
reversible: true

phases:
  - id: phase-0
    name: "Structural Foundation"
    duration_hours: 2
    dependencies: []
  - id: phase-1
    name: "Move Shared Concerns"
    duration_hours: 3
    dependencies: [phase-0]
  - id: phase-2
    name: "Structure Core Module"
    duration_hours: 12
    dependencies: [phase-0]
  - id: phase-3
    name: "Create Composition Pattern"
    duration_hours: 4
    dependencies: [phase-2]
  - id: phase-4
    name: "Refactor RoleManagement"
    duration_hours: 5
    dependencies: [phase-1]
  - id: phase-5
    name: "Refactor Integrations"
    duration_hours: 8
    dependencies: [phase-0]
  - id: phase-6
    name: "Tests & Cleanup"
    duration_hours: 6
    dependencies: [phase-2, phase-4, phase-5]

success_criteria:
  - no_projects_in_src_core: true
  - composition_pattern_implemented: true
  - all_features_have_model_usecases_ports_adapters: true
  - dotnet_build_succeeds: true
  - dotnet_test_succeeds: true
  - dotnet_format_succeeds: true
```

---

## Overview

**Cíl**: Přesunout a reorganizovat obsah z `src/Core` → `src/Modules/Core` a ostatní moduly do nové feature-sliced struktury.

**Timeline**: 6 fází, kterou lze dělat paralelně (features se odvádějí nezávisle).

**Risk Level**: ⚠️ **Střední** — změna struktura a namespaces, ale logika se nemění (jen reorganizace).

---

## Fáze 0 — Structural Foundation (Příprava)

**Phase ID**: `phase-0` | **Duration**: 2 hours | **Risk**: Low

### Cíl
Vytvořit novou adresářovou strukturu a update Solution file.

### Kroky (CLI-friendly)

| Krok | Operace | Příkaz | Validace |
|------|---------|--------|----------|
| 0.1 | Vytvoř `src/Modules` | `mkdir -p src/Modules` | `Test-Path src/Modules` |
| 0.2 | Vytvoř `src/Shared/Libs` | `mkdir -p src/Shared/Libs` | `Test-Path src/Shared/Libs` |
| 0.3 | Move Core | `Move-Item -Path src/Core -Destination src/Modules/Core` | `Test-Path src/Modules/Core/Core.csproj` |
| 0.4 | Update .slnx | Ručně: `src/Core/Core.csproj` → `src/Modules/Core/Core.csproj` | `grep -q "src/Modules/Core" UserOrganization.slnx` |
| 0.5 | RootNamespace | File: `src/Modules/Core/Core.csproj` - `<RootNamespace>UserOrg.Core</RootNamespace>` | Kontrola XML |
| 0.6 | Build test | `dotnet clean && dotnet restore && dotnet build` | Exit code: 0 |

### ✅ Checklist Phase 0

| Kontrola | Status | Příkaz validace |
|----------|--------|-----------------|
| `src/Modules` existuje | ☐ | `Test-Path src/Modules -PathType Container` |
| `src/Shared/Libs` existuje | ☐ | `Test-Path src/Shared/Libs -PathType Container` |
| `src/Core` → `src/Modules/Core` | ☐ | `Test-Path src/Modules/Core/Core.csproj` |
| `.slnx` má správnou cestu | ☐ | `grep src/Modules/Core UserOrganization.slnx` |
| Core.csproj RootNamespace | ☐ | `grep "UserOrg.Core" src/Modules/Core/Core.csproj` |
| `dotnet build` úspěšně | ☐ | `dotnet build --no-restore` |

### Git Checkpoint Phase 0
```bash
git add .
git commit -m "build: phase-0 structural foundation - move Core to src/Modules/Core"
```

---

## Fáze 1 — Move Shared Concerns (Move do Shared)

**Phase ID**: `phase-1` | **Duration**: 3 hours | **Risk**: Low-Medium | **Depends on**: phase-0

### Cíl
Přesunout cross-cutting moduly (Forge.Core → RoleManagement) do `src/Shared/Libs`.

### Kroky (CLI-friendly)

| Krok | Operace | Příkaz | Validace |
|------|---------|--------|----------|
| 1.1 | Nový projekt RoleManagement | `dotnet new classlib -n UserOrg.RoleManagement -o src/Shared/Libs/RoleManagement` | `Test-Path src/Shared/Libs/RoleManagement/RoleManagement.csproj` |
| 1.2 | Vytvoř strukturu | `mkdir -p src/Shared/Libs/RoleManagement/{Model,UseCases,Ports,Adapters,Composition,Tests}` | Dir. tree OK |
| 1.3 | Přesuň Forge.Core obsah | `Copy-Item src/Modules/Libraries/Forge.Core/*.cs src/Shared/Libs/RoleManagement/Model/` | Počet souborů OK |
| 1.4 | Update namespaces | Ručně: `using Forge.Core;` → `using UserOrg.Shared.RoleManagement.Model;` | `grep -r "Forge.Core" src/` (empty) |
| 1.5 | Přidej do solution | `dotnet sln add src/Shared/Libs/RoleManagement/RoleManagement.csproj` | `grep RoleManagement UserOrganization.slnx` |
| 1.6 | Update refs v .csproj | Update Gateway/API aby referencovaly `RoleManagement.csproj` | `grep RoleManagement src/Gateway/Gateway.csproj` |
| 1.7 | Build test | `dotnet clean && dotnet restore && dotnet build` | Exit code: 0 |

### ✅ Checklist Phase 1

| Kontrola | Status | Příkaz |
|----------|--------|--------|
| RoleManagement projekt existuje | ☐ | `Test-Path src/Shared/Libs/RoleManagement/RoleManagement.csproj` |
| Struktura: Model/UseCases/... | ☐ | `ls src/Shared/Libs/RoleManagement/ \| grep Model` |
| Forge.Core obsah přesunut | ☐ | `ls src/Shared/Libs/RoleManagement/Model/ \| wc -l` |
| Namespaces updatovány | ☐ | `grep -r "Forge.Core" src/ --include="*.cs" \| wc -l` (=0) |
| Solution file updatován | ☐ | `grep "RoleManagement.csproj" UserOrganization.slnx` |
| Project refs OK | ☐ | `grep RoleManagement src/API/API.csproj` |
| Build OK | ☐ | `dotnet build --no-restore` |

### Git Checkpoint Phase 1
```bash
git add .
git commit -m "refactor: phase-1 move Forge.Core to src/Shared/Libs/RoleManagement"
```

---

## Fáze 2 — Structure Core Module (Reorganizace Core)

**Phase ID**: `phase-2` | **Duration**: 12 hours | **Risk**: Medium | **Depends on**: phase-0

### Cíl
Reorganizovat `src/Modules/Core` z Domain/Application/Infrastructure na feature-sliced Model/UseCases/Ports/Adapters/Api.

### Feature-by-Feature Loop (pro každou feature: Users, Roles, Projects, ...)

| Feature | Model Moved | Ports | UseCases | Adapters | Api | Namespaces | Build OK |
|---------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Users | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Roles | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Projects | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| Permissions | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |

### Per-Feature Steps (repetuj pro každou feature)

```yaml
# Pro feature: Users
steps:
  2.1:
    op: "Vytvoř strukturu"
    cmd: "mkdir -p src/Modules/Core/Features/Users/{Model,UseCases,Ports,Adapters,Api,Tests}"
    validate: "ls src/Modules/Core/Features/Users/"
    
  2.2:
    op: "Move Model (User.cs z Domain)"
    cmd: "mv src/Modules/Core/Domain/User.cs src/Modules/Core/Features/Users/Model/"
    validate: "Test-Path src/Modules/Core/Features/Users/Model/User.cs"
    namespace: "UserOrg.Core.Features.Users.Model"
    
  2.3:
    op: "Move Ports (IUserRepository z Domain.Abstractions)"
    cmd: "mv src/Modules/Core/Domain/Abstractions/IUserRepository.cs src/Modules/Core/Features/Users/Ports/"
    validate: "Test-Path src/Modules/Core/Features/Users/Ports/IUserRepository.cs"
    namespace: "UserOrg.Core.Features.Users.Ports"
    
  2.4:
    op: "Move UseCases (CreateUserService z Application)"
    cmd: "mv src/Modules/Core/Application/Users/CreateUserService.cs src/Modules/Core/Features/Users/UseCases/"
    validate: "Test-Path src/Modules/Core/Features/Users/UseCases/CreateUserService.cs"
    namespace: "UserOrg.Core.Features.Users.UseCases"
    using_updates: ["using UserOrg.Core.Features.Users.Ports;", "using UserOrg.Core.Features.Users.Model;"]
    
  2.5:
    op: "Move Adapters (UserRepository z Infrastructure.DataAccess)"
    cmd: "mv src/Modules/Core/Infrastructure.DataAccess/Repositories/UserRepository.cs src/Modules/Core/Features/Users/Adapters/"
    validate: "Test-Path src/Modules/Core/Features/Users/Adapters/UserRepository.cs"
    namespace: "UserOrg.Core.Features.Users.Adapters"
    depends_on: "Persistence/Entities/UserEntity.cs"
    
  2.6:
    op: "Move Api (CreateUserEndpoint z API)"
    cmd: "mv src/Modules/Core/API/Endpoints/CreateUserEndpoint.cs src/Modules/Core/Features/Users/Api/"
    validate: "Test-Path src/Modules/Core/Features/Users/Api/CreateUserEndpoint.cs"
    namespace: "UserOrg.Core.Features.Users.Api"
    
  2.7:
    op: "Persistence centrálně"
    cmd: "mkdir -p src/Modules/Core/Persistence/{Db,Entities,EfCoreConfigurations}"
    cmd2: "mv src/Modules/Core/Infrastructure.DataAccess/Db/* src/Modules/Core/Persistence/Db/"
    validate: "ls src/Modules/Core/Persistence/Db/"
    
  2.8:
    op: "Build per-feature"
    cmd: "dotnet build --no-restore"
    expected_exit_code: 0

cleanup:
  3.1: "rm -r src/Modules/Core/Domain"
  3.2: "rm -r src/Modules/Core/Application"
  3.3: "rm -r src/Modules/Core/Infrastructure.DataAccess"
  validate: "ls src/Modules/Core/ | grep -E 'Domain|Application|Infrastructure' | wc -l  # expect: 0"
```

### ✅ Checklist Phase 2

```checklist
STRUCTURE:
  [ ] Všechny features mají: Features/<Feature>/{Model,UseCases,Ports,Adapters,Api,Tests}
  [ ] Persistence centrálně: src/Modules/Core/Persistence/{Db,Entities,EfCoreConfigurations}
  [ ] Staré Domain/Application/Infrastructure smazány

CODE:
  [ ] Všechny namespaces updatovány: grep -r "Domain\." src/Modules/Core/Features/
  [ ] Všechny usings korektní: grep "using UserOrg.Core.Application" src/Modules/Core/ (empty)
  [ ] IUserRepository v Ports/, UserRepository v Adapters/

BUILD & TEST:
  [ ] dotnet build --no-restore (exit 0)
  [ ] dotnet test Tests/Core/ (compile OK, testy projdou)
```

### Git Checkpoint Phase 2
```bash
git add .
git commit -m "refactor: phase-2 restructure Core to feature-sliced pattern

- Create Features/<Feature>/{Model,UseCases,Ports,Adapters,Api,Tests}
- Centralize Persistence/Db, Entities, EfCoreConfigurations
- Remove Domain/, Application/, Infrastructure.DataAccess/
- Update all namespaces and using statements
"
```

---

## Fáze 3 — Create Composition Pattern (DI + Endpoints)

**Phase ID**: `phase-3` | **Duration**: 4 hours | **Risk**: Low | **Depends on**: phase-2

### Cíl
Vytvoř `Composition/` v Core modulu s `AddCore()` DI registrací a `MapCoreEndpoints()` mapováním.

### Kroky

| Krok | Operace | Příkaz / Soubor | Validace |
|------|---------|-----------------|----------|
| 3.1 | Vytvoř Composition | `mkdir -p src/Modules/Core/Composition` | `Test-Path` |
| 3.2 | CoreModule.cs | Vytvoř soubor s `AddCore()` extension | `grep "AddCore" src/Modules/Core/Composition/CoreModule.cs` |
| 3.3 | CoreEndpoints.cs | Vytvoř soubor s `MapCoreEndpoints()` extension | `grep "MapCoreEndpoints" src/Modules/Core/Composition/CoreEndpoints.cs` |
| 3.4 | Update API/Program.cs | Add `.AddCore()` v DI + `.MapCoreEndpoints()` | `grep "AddCore\|MapCoreEndpoints" src/API/Program.cs` |
| 3.5 | Update Gateway/Program.cs | Totéž jako 3.4 | `grep "AddCore\|MapCoreEndpoints" src/Gateway/Program.cs` |
| 3.6 | Build & Run | `dotnet build && dotnet run --project src/API/API.csproj` | HTTP 200 na health check |

### ✅ Checklist Phase 3

```checklist
FILES:
  [ ] src/Modules/Core/Composition/CoreModule.cs existuje
  [ ] src/Modules/Core/Composition/CoreEndpoints.cs existuje

CODE:
  [ ] CoreModule.AddCore() registruje všechny services (IUserRepository, Services...)
  [ ] CoreEndpoints.MapCoreEndpoints() mapuje všechny endpoints
  [ ] API/Program.cs: services.AddCore()
  [ ] API/Program.cs: app.MapCoreEndpoints()
  [ ] Gateway/Program.cs: services.AddCore()
  [ ] Gateway/Program.cs: app.MapCoreEndpoints()

BUILD & VERIFY:
  [ ] dotnet build --no-restore (exit 0)
  [ ] dotnet run src/API/API.csproj (starts OK)
  [ ] http://localhost:5000/health (returns 200)
```

### Git Checkpoint Phase 3
```bash
git add .
git commit -m "refactor: phase-3 add composition pattern

- Create Core/Composition/CoreModule.cs (AddCore)
- Create Core/Composition/CoreEndpoints.cs (MapCoreEndpoints)
- Update API/Program.cs to register and map
- Update Gateway/Program.cs to register and map
"
```

---

## Fáze 4 — Refactor RoleManagement (Shared Library)

**Phase ID**: `phase-4` | **Duration**: 5 hours | **Risk**: Low | **Depends on**: phase-1

### Cíl
Finalizovat RoleManagement v `src/Shared/Libs/`.

| Krok | Operace | Příkaz / Akce | Validace |
|------|---------|---------------|----------|
| 4.1 | Strukturuj features | `mkdir -p src/Shared/Libs/RoleManagement/Features/{Roles,Permissions}` | Dir list |
| 4.2 | Move Model | Přesuň Role, Permission do Model/ | `ls src/Shared/Libs/RoleManagement/Model/` |
| 4.3 | Move UseCases | Orchestrace services | `ls src/Shared/Libs/RoleManagement/UseCases/` |
| 4.4 | Move Adapters | Repository implementace | `ls src/Shared/Libs/RoleManagement/Adapters/` |
| 4.5 | Create Composition | `RoleManagementModule.cs`, `AddRoleManagement()` | `grep AddRoleManagement` |
| 4.6 | Update API/Program.cs | Add `.AddRoleManagement()` | `grep AddRoleManagement src/API/Program.cs` |
| 4.7 | Build test | `dotnet build --no-restore` | Exit 0 |

### Git Checkpoint Phase 4
```bash
git commit -m "refactor: phase-4 finalize RoleManagement shared library"
```

---

## Fáze 5 — Refactor Integrations (Heappe)

**Phase ID**: `phase-5` | **Duration**: 8 hours | **Risk**: Medium | **Depends on**: phase-0

### Cíl
Přesunout Heappe integration do `src/Modules/Integration/Heappe` s feature-sliced strukturou.

| Krok | Operace | Příkaz | Validace |
|------|---------|--------|----------|
| 5.1 | Move Heappe | Move z aktuální lokace do `src/Modules/Integration/Heappe` | `Test-Path src/Modules/Integration/Heappe/Heappe.csproj` |
| 5.2 | Create struktura | `mkdir -p {Model,UseCases,Ports,Adapters,Composition}` | Dir list |
| 5.3 | Reorganizuj obsah | Model/UseCases/Ports/Adapters (jako Core) | Feature-sliced OK |
| 5.4 | Create Composition | `AddHeappe()`, `MapHeappeEndpoints()` | Grep příkazy |
| 5.5 | Update API/Program.cs | `.AddHeappe()` + `.MapHeappeEndpoints()` | Grep ověření |
| 5.6 | Build test | `dotnet build --no-restore` | Exit 0 |

### Git Checkpoint Phase 5
```bash
git commit -m "refactor: phase-5 move Heappe to src/Modules/Integration with composition"
```

---

## Fáze 6 — Tests & Cleanup

**Phase ID**: `phase-6` | **Duration**: 6 hours | **Risk**: Low | **Depends on**: phase-2, phase-4, phase-5

### Cíl
Finalizovat testy, ověřit vše, cleanup.

| Krok | Operace | Příkaz | Validace |
|------|---------|--------|----------|
| 6.1 | Update test namespaces | Grep/replace testy na nový namespace | Zero stale namespaces |
| 6.2 | Run tests | `dotnet test --no-build` | All pass |
| 6.3 | Format check | `dotnet format --verify-no-changes` | Zero changes |
| 6.4 | Clean cycle | `dotnet clean && dotnet restore && dotnet build` | Exit 0 |
| 6.5 | Final cleanup | Smaž redundantní .csproj files | Only needed remain |

### ✅ Checklist Phase 6

```checklist
CODE:
  [ ] Všechny test namespaces updatovány
  [ ] dotnet test projde (0 failures)
  [ ] dotnet format --verify-no-changes (0 changes)
  [ ] dotnet build (0 errors)

STRUCTURE:
  [ ] Žádné projekty v src/Core/ (jen v src/Modules/Core)
  [ ] Žádné v src/Modules/Libraries/ (jen Core, Integration)
  [ ] src/Shared/Libs/RoleManagement existuje a funguje

FINAL:
  [ ] Git log: 6 clear commits (phase-0 až phase-6)
  [ ] All refs updated: grep -r "old.namespace" src/ (empty)
```

### Git Checkpoint Phase 6
```bash
git commit -m "refactor: phase-6 tests, cleanup, finalization

- Update all test namespaces
- Verify dotnet build, test, format
- Remove redundant .csproj files
- Clean directory structure
"
```

---

## 🛡️ Safety Guidelines — Jak se chránit

### PRE-Refactoring Checklist

```yaml
before_start:
  step_1: "Branch creation"
  command: "git checkout -b refactor/core-feature-sliced-v1"
  validation: "git branch | grep refactor/core"
  
  step_2: "Backup branch"
  command: "git branch backup-original && git checkout backup-original && git commit --allow-empty -m 'backup: original state before refactoring'"
  validation: "git branch | grep backup-original"
  
  step_3: "Verify clean build"
  command: "dotnet clean && dotnet restore && dotnet build --no-restore"
  expected_exit_code: 0
  
  step_4: "Verify tests pass"
  command: "dotnet test --no-build"
  expected_exit_code: 0
  
  step_5: "Check git status"
  command: "git status"
  expected_output: "working tree clean"
```

### During Refactoring Safety Rules

| Rule | Why | How |
|------|-----|-----|
| Small steps | Less to debug if fails | One feature/phase per commit |
| Test after each move | Catch errors early | `dotnet build` after each step |
| Commit frequently | Easy rollback | Commit per logical change |
| Use IDE refactor | Catch all references | Ctrl+. in VS Code, not manual |
| Check namespaces | Prevent compile errors | Grep before moving on |

### If Something Goes Wrong

```bash
# Option 1: Rollback single file
git checkout -- src/Modules/Core/Features/Users/Model/User.cs

# Option 2: Rollback uncommitted changes
git reset --hard HEAD

# Option 3: Rollback entire phase commit
git revert <commit-hash>

# Option 4: Go back to backup branch
git checkout backup-original
git reset --hard HEAD~1  # Back before refactoring
```

---

## 🔧 Workflow Commands — Copy-Paste Ready

### Full Refactoring Sequence (All Phases)

```bash
#!/bin/bash
# Full refactoring script (execute step-by-step, not all at once)

set -e  # Exit on first error

REPO_ROOT=$(pwd)
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
LOG_FILE="refactoring_${TIMESTAMP}.log"

log() { echo "[$(date +'%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"; }

# === PHASE 0 ===
log "Starting PHASE 0: Structural Foundation"
mkdir -p src/Modules
mkdir -p src/Shared/Libs
Move-Item -Path "src/Core" -Destination "src/Modules/Core" 2>/dev/null || mv src/Core src/Modules/Core
git add .
git commit -m "build: phase-0 structural foundation" || true
dotnet clean && dotnet restore && dotnet build --no-restore || { log "Phase 0 FAILED"; exit 1; }
log "✓ PHASE 0 Complete"

# === PHASE 1 ===
log "Starting PHASE 1: Move Shared Concerns"
dotnet new classlib -n UserOrg.RoleManagement -o src/Shared/Libs/RoleManagement
mkdir -p src/Shared/Libs/RoleManagement/{Model,UseCases,Ports,Adapters,Composition,Tests}
rm -f src/Shared/Libs/RoleManagement/Class1.cs
# [Move Forge.Core content]
dotnet sln add src/Shared/Libs/RoleManagement/RoleManagement.csproj
dotnet build --no-restore || { log "Phase 1 FAILED"; exit 1; }
git add .
git commit -m "refactor: phase-1 move Shared Concerns" || true
log "✓ PHASE 1 Complete"

# === PHASE 2 ===
log "Starting PHASE 2: Structure Core Module"
# [For each feature: Users, Roles, Projects, Permissions]
# mkdir -p src/Modules/Core/Features/<Feature>/{Model,UseCases,Ports,Adapters,Api,Tests}
# [Move code and update namespaces]
mkdir -p src/Modules/Core/Persistence/{Db,Entities,EfCoreConfigurations}
rm -rf src/Modules/Core/{Domain,Application,Infrastructure.DataAccess}
dotnet build --no-restore || { log "Phase 2 FAILED"; exit 1; }
git add .
git commit -m "refactor: phase-2 structure Core module" || true
log "✓ PHASE 2 Complete"

# === PHASE 3 ===
log "Starting PHASE 3: Composition Pattern"
mkdir -p src/Modules/Core/Composition
# [Create CoreModule.cs, CoreEndpoints.cs]
# [Update API/Program.cs, Gateway/Program.cs]
dotnet build --no-restore || { log "Phase 3 FAILED"; exit 1; }
git add .
git commit -m "refactor: phase-3 composition pattern" || true
log "✓ PHASE 3 Complete"

# === PHASE 4 ===
log "Starting PHASE 4: RoleManagement"
# [Finalize RoleManagement structure]
dotnet build --no-restore || { log "Phase 4 FAILED"; exit 1; }
git add .
git commit -m "refactor: phase-4 finalize RoleManagement" || true
log "✓ PHASE 4 Complete"

# === PHASE 5 ===
log "Starting PHASE 5: Heappe Integration"
# [Move Heappe to src/Modules/Integration/Heappe]
dotnet build --no-restore || { log "Phase 5 FAILED"; exit 1; }
git add .
git commit -m "refactor: phase-5 Heappe integration" || true
log "✓ PHASE 5 Complete"

# === PHASE 6 ===
log "Starting PHASE 6: Tests & Cleanup"
dotnet test --no-build || { log "Tests FAILED"; exit 1; }
dotnet format --verify-no-changes || { log "Format check FAILED"; exit 1; }
dotnet clean && dotnet restore && dotnet build || { log "Final build FAILED"; exit 1; }
git add .
git commit -m "refactor: phase-6 tests and cleanup" || true
log "✓ PHASE 6 Complete - SUCCESS"

log "=== REFACTORING COMPLETE ==="
log "Log written to: $LOG_FILE"
```

### Per-Phase Validation Commands

```bash
# Phase 0 validation
dotnet build --no-restore && echo "✓ Build OK"
grep -q "src/Modules/Core" UserOrganization.slnx && echo "✓ .slnx updated"

# Phase 1 validation
test -f src/Shared/Libs/RoleManagement/RoleManagement.csproj && echo "✓ RoleManagement exists"
grep -r "Forge.Core" src/ --include="*.cs" | wc -l && echo "Old refs count (expect: 0)"

# Phase 2 validation
find src/Modules/Core/Features -type d -name "Model" | wc -l && echo "Features with Model/"
grep -r "using UserOrg.Core.Domain" src/Modules/Core/ --include="*.cs" | wc -l && echo "Old Domain refs (expect: 0)"

# Phase 3 validation
test -f src/Modules/Core/Composition/CoreModule.cs && echo "✓ CoreModule.cs exists"
grep "AddCore" src/API/Program.cs && echo "✓ API registers AddCore"

# Phase 6 final validation
dotnet build --no-restore && echo "✓ Build OK"
dotnet test --no-build && echo "✓ Tests OK"
dotnet format --verify-no-changes && echo "✓ Format OK"
```

---

## Common Pitfalls & Solutions

| Problém | Příčina | Řešení | Příkaz validace |
|---------|--------|--------|----------|
| "type X not found" | Namespace outdated | IDE refactor (Ctrl+.) | `grep "using UserOrg.X" src/` |
| IUserRepository private | Visibility issue | Add `public` / `internal` | `grep "class IUserRepository" src/` |
| .csproj reference broken | Path incorrect | Check `<ProjectReference>` | `grep ProjectReference src/API/API.csproj` |
| Circular reference (A→B→A) | Module depends on itself | Extract to Shared/Libs | `grep -l "ProjectReference" src/*/` |
| DbContext not found | Namespace wrong | Check `Persistence/Db/` | `find src -name "*DbContext.cs"` |
| Test compile error | Old namespace in test | Update `using` statements | `grep "using UserOrg.Core.API" Tests/` |

---

## Success Criteria per Phase

### Phase 0 ✓
```bash
test -d src/Modules && \
test -d src/Shared/Libs && \
test -f src/Modules/Core/Core.csproj && \
dotnet build --no-restore && \
echo "✓✓✓ Phase 0 Success"
```

### Phase 1 ✓
```bash
test -f src/Shared/Libs/RoleManagement/RoleManagement.csproj && \
dotnet build --no-restore && \
grep -q "RoleManagement.csproj" UserOrganization.slnx && \
echo "✓✓✓ Phase 1 Success"
```

### Phase 2 ✓
```bash
find src/Modules/Core/Features -type d -name "Ports" | wc -l | grep -E "[1-9]" && \
find src/Modules/Core/Features -type d -name "Adapters" | wc -l | grep -E "[1-9]" && \
! test -d src/Modules/Core/Domain && \
! test -d src/Modules/Core/Application && \
dotnet build --no-restore && \
echo "✓✓✓ Phase 2 Success"
```

### Phase 3 ✓
```bash
test -f src/Modules/Core/Composition/CoreModule.cs && \
test -f src/Modules/Core/Composition/CoreEndpoints.cs && \
grep -q "AddCore" src/API/Program.cs && \
dotnet run --project src/API/API.csproj &
sleep 3 && curl http://localhost:5000/health && \
echo "✓✓✓ Phase 3 Success"
```

### Phase 6 ✓
```bash
dotnet test --no-build && \
dotnet format --verify-no-changes && \
dotnet build --no-restore && \
git log --oneline | grep -E "phase-[0-6]" | wc -l | grep "6" && \
echo "✓✓✓ ALL PHASES SUCCESS"
```

---

## 🎯 Final Summary

**Total Phases**: 6  
**Total Duration**: ~40 hours  
**Git Commits Required**: 6 (one per phase)  
**Risk Level**: Medium (but reversible)  
**Success Definition**: All phases complete with zero breaking changes

```yaml
execution_summary:
  start: "Phase 0 - Structural Foundation"
  sequence: [phase-0, phase-1, phase-2, phase-3, phase-4, phase-5, phase-6]
  end: "Phase 6 - Tests & Cleanup"
  
  validation_gates:
    before_phase_0: "dotnet build"
    between_phases: "dotnet build --no-restore"
    after_phase_6: "dotnet test && dotnet format --verify-no-changes && dotnet build"
  
  rollback_path: "git checkout backup-original"
  success_path: "All phases complete, all tests pass, format clean"
```

---

## 📚 Resources & Links

| Resource | Purpose | Path |
|----------|---------|------|
| ARCHITECTURE_GUIDE.md | Team onboarding, "where is...", patterns | [ARCHITECTURE_GUIDE.md](./ARCHITECTURE_GUIDE.md) |
| CORE_ARCHITECTURE_COMBINED.md | Why/what/how — comprehensive reference | [CORE_ARCHITECTURE_COMBINED.md](../CORE_ARCHITECTURE_COMBINED.md) |
| minimal-api-csharp SKILL | Endpoint structure conventions | [.github/skills/minimal-api-csharp/SKILL.md](../../.github/skills/minimal-api-csharp/SKILL.md) |
| refactor SKILL | Refactoring best practices | [.github/skills/refactor/SKILL.md](../../.github/skills/refactor/SKILL.md) |
| git-commit SKILL | Conventional commits | [.github/skills/git-commit/SKILL.md](../../.github/skills/git-commit/SKILL.md) |
| csharp.instructions | C# code style | [.github/instructions/csharp.instructions.md](../../.github/instructions/csharp.instructions.md) |
| dotnet-architecture-good-practices | DDD & .NET patterns | [.github/instructions/dotnet-architecture-good-practices.instructions.md](../../.github/instructions/dotnet-architecture-good-practices.instructions.md) |

---

## ❓ FAQ (CLI & Developer)

### Structure & Organization

**Q: Jsou taky test reference soubory?**  
A: Ano. Testy zůstaneme v `Features/*/Tests/` a testují daný feature. Update navespaces během refactoringu.  
Validace: `find src/Modules/Core/Features -name "*Tests.cs" | head -5`

**Q: Smažu vůbec Domain/Application/Infrastructure adresáře?**  
A: Ano, úplně. Poté co obsah přesunete do feature-sliced struktury (Model/UseCases/...), staré nejsou potřeba.  
Validace: `ls -la src/Modules/Core/ | grep -E "Domain|Application|Infrastructure"`  (očekávám: nic)

**Q: Všechny endpointy musí být v Api/?**  
A: Ano. HTTP handling patří do Api/ vrstvy. Domain logika v Model/, orchestrace v UseCases/.  
Validace: `find src/Modules/Core/Features -name "*Endpoint.cs" | wc -l`

**Q: Jak handleju circular dependencies?**  
A: Při circular (A→B→C→A) extrahuj shared kód do `src/Shared/Libs`. Dependency: Feature → Shared (ne opačně).  
Validace: `dotnet build` by měl projít bez "circular reference" erroru.

### Práce & Workflow

**Q: Smažu staré testy?**  
A: Ne! Jen je reorganizuješ do `Features/*/Tests/` a updatuješ references.  
Příkaz: `find Tests/ -name "*UserTests.cs" -exec grep -l "UserOrg.Core.Application" {} \;`  (updatuj namespace)

**Q: Jak validuji, že je vše OK?**  
A: Spusť validační příkazy per-phase:  
```bash
dotnet build --no-restore && \
dotnet test --no-build && \
dotnet format --verify-no-changes && \
echo "✓ All OK"
```

**Q: Kolik jsou commits?**  
A: 6 commitů (jeden per phase), jasně pojmenované: `build: phase-0`, `refactor: phase-1`, atd.  
Příkaz: `git log --oneline | grep -E "phase-[0-6]" | wc -l`  (očekávám: 6)

**Q: Co když cokoliv selže?**  
A: Máš backup branch (`backup-original`). Rollback:  
```bash
git checkout backup-original
git reset --hard HEAD~1
```

### Technické Details

**Q: Jak handleju DbContext migrations?**  
A: `DbContext` zůstane v `Persistence/Db/UserOrgDbContext.cs`. Migrations se přesunují s ním.  
Validace: `dotnet ef migrations list --project src/Modules/Core`

**Q: Co když má feature svou DB schéma?**  
A: Zatím vše do centrálního `UserOrgDbContext`. Pokud budete později splitovat DB, toto si zachovejte.  
Doporučení: Dokumentujte dependency v commitu.

**Q: Jak testy upravím?**  
A: Testovací framework (xUnit) zůstane stejný. Update jen namespaces a references:  
```bash
find Tests/Core -name "*.cs" -exec sed -i 's/using UserOrg.Core.Application/using UserOrg.Core.Features.*.UseCases/g' {} \;
```

---

## 🚀 Quick Start (TL;DR)

**Máte 1 hodinu?** Začněte Phase 0:
```bash
git checkout -b refactor/core-feature-sliced
mkdir -p src/Modules src/Shared/Libs
mv src/Core src/Modules/Core
dotnet clean && dotnet restore && dotnet build
git add . && git commit -m "build: phase-0 structural foundation"
```

**Máte víc času?** Postupujte fází po fázi podle schématu výše.

**Chcete skript?** Použijte bash script z "Workflow Commands" sekcí níže a spouštějte po fazích.

---

## Troubleshooting Checklist

Pokud se zasekneš:

```bash
# 1. Check current state
dotnet build --diagnose

# 2. Look for old namespaces
grep -r "using UserOrg.Core.Application" src/ --include="*.cs" | wc -l
grep -r "using UserOrg.Core.Domain" src/ --include="*.cs" | wc -l
grep -r "using Forge.Core" src/ --include="*.cs" | wc -l
# Všechny by měly být 0

# 3. Verify project references
grep -r "ProjectReference" src/API/API.csproj src/Gateway/Gateway.csproj

# 4. List all features
find src/Modules/Core/Features -maxdepth 1 -type d

# 5. Verify structure
find src/Modules/Core/Features -type d -name "Model" | wc -l
find src/Modules/Core/Features -type d -name "Api" | wc -l
# Měl by být stejný počet

# 6. Final health check
dotnet clean && dotnet restore && dotnet build --no-restore && echo "✓ Build OK"
```

---

## Support & Escalation

| Problém | Contacts | Action |
|---------|----------|--------|
| Compile error | IDE, `dotnet build` output | Zkontroluj error message, likely namespace |
| Test failure | `dotnet test --no-build` | Zkontroluj test references, mock setup |
| Circular dependency | Architecture review | Extract to Shared/Libs |
| Merge conflict | Git | Resolve manually, re-run build |
| Stuck/lost | Backup branch | `git checkout backup-original` |

---

**Last Updated**: 2026-02-19  
**Version**: 1.0  
**Status**: Complete & CLI-Friendly ✓
