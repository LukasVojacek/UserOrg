# Implementation plan — Project tag create/assign split + read-only freeze + validation cleanup

**Branch:** `feat/refactor-core` · **Base:** `develop` · **Date:** 2026-06-03
**Decisions source:** grill session (CONTEXT.md *Project tagging*, [ADR-0007](../architecture/adr/0007-project-tags-by-name-create-vs-assign.md)).
**Commit rule:** conventional-commit, **no `Co-Authored-By` trailer**. Commit only on explicit request.

Goal: bring the implemented v2 Project tag surface in line with the agreed design — separate **create** (Owner/Manager/Admin) from **assign** (all project roles), freeze tags on per-project read-only, and remove duplicated validation + a dead handler abstraction.

---

## Commit 1 — `refactor(core): drop redundant AssignedProviderId shadow-sync` (decision 3, standalone)

Already present in the working tree (the deletion in `DatabaseContext.UnitOfWork.cs`). Isolate it so the feature commit stays surgical.

1. Stage **only** `src/Modules/Core.DataAccess/Contexts/DatabaseContext.UnitOfWork.cs`.
   - Rationale: `UserConfiguration.cs:60-69` maps column `AssignedProviderId` to the `_assignedProviderId` field and `Ignore`s the `AssignedProviderIds` list, so domain writes persist directly; the shadow-sync block was dead and even referenced a non-existent property name.
   - **Verify:** `dotnet build` green; `Core.UnitTests` no new failures (8 pre-existing `*DatabaseAccessTests` remain).
   - Optional (deferred unless asked): round-trip test `PromoteToProviderAdmin` → save → `AssignedProviderId` column populated.

---

## Commit 2 — `feat(core): split project tag create vs assign, freeze tags on read-only`

### Step 1 — Value objects: single source of truth for format validation (decision 5)
Files: `src/Modules/Core.Domain/Project/TagName.cs`, `DomainTypeName.cs`.

- Add `public static bool TryCreate(string? value, out TagName result, out string? error)` next to `Create` (same rules: non-empty, ≤100, `Trim()`); on failure set `result = default`, `error = "<message>"`, return `false`. Mirror for `DomainTypeName`.
- Keep `Create` (throws) for internal/domain callers.
- **Verify:** unit tests for `TryCreate` happy/empty/too-long.

### Step 2 — Domain: read-only guard + idempotency alignment + drop dead handler (decisions 4, 6)
File: `src/Modules/Core.Domain/Project/Project.cs`, `IProjectTagHandler.cs`.

- Replace `AddTagAsync`/`RemoveTagAsync` with:
  - `public async Task<Result<Error>> AssignTagAsync(TagName, UserName createdBy, IProjectTagHandler handler)` — **create-and-assign** (POST path). Order: `EnsureNotReadOnly()` → idempotency via `HasTag(tagName)` (already filters `!IsDeleted`) → `handler.EnsureTagExistsAsync` → add to `_projectTags`.
  - `public Result<Error> AssignExistingTag(ProjectTag tag)` — **assign-existing** (PUT path), synchronous. `EnsureNotReadOnly()` → idempotency (`HasTag`) → add. Caller resolves the tag (404 if missing) before calling.
  - `public Result<Error> RemoveTag(TagName)` — synchronous. `EnsureNotReadOnly()` → if `HasTag` remove from `_projectTags` (EF deletes the join row); idempotent no-op otherwise. **No handler** — removal was always driven by the tracked collection.
- Fix the idempotency guard to use `HasTag` (`!IsDeleted`) so it agrees with the `Tags` projection.
- `IProjectTagHandler`: keep `EnsureTagExistsAsync(TagName, UserName)`; **add** `Task<ProjectTag?> FindTagAsync(TagName name)`; **remove** the always-`true` `RemoveTagAssociationAsync`.
- **Verify:** domain unit tests — assign creates/idempotent, assign-existing, remove idempotent, each returns `ProjectErrors.ProjectIsReadOnly` when `Mode == ReadOnly`.

### Step 3 — DataAccess: handler + DI (decisions 1, 6)
File: `src/Modules/Core.DataAccess/Contexts/DatabaseContext.ProjectTagHandler.cs`.

- Implement `FindTagAsync` = `Set<ProjectTag>().FirstOrDefaultAsync(t => t.Name == name)` (unique alternate key on `Name`).
- Keep `EnsureTagExistsAsync` (find-or-create, shadow `Id` V7 + `CreatedByUserId`).
- Remove `RemoveTagAssociationAsync` implementation.
- DI in `ServiceCollectionExtensions.cs` unchanged (still one `IProjectTagHandler`).
- **Verify:** `dotnet build` green.

### Step 4 — Endpoints: split into POST (create) + PUT (assign-existing) (decisions 1, 2, 5)
Folder: `src/Modules/Core/Features/Project/`.

- **New** `CreateProjectTag/CreateProjectTagEndpoint.cs` — `POST Projects/{projectShortName}/Tags`:
  - `record Request([FromRoute] ProjectShortName ProjectShortName, [FromBody] Body Body)`, `record Body(string Name)`.
  - Scope check (404), `TagName.TryCreate` → 400, `repository.FindAsync` (404), resolve actor `UserName` via `IUserInfoQuery.GetBySubAsync`, `project.AssignTagAsync(...)` → `.ToProblem()` on failure (409 read-only), save, evict cache. Return `204`.
  - `.DisabledWhen("ReadOnlyMode")` + docs (`Produces` 204/400/404/409). `WithName("CreateProjectTagV2")`.
- **Modify** `AssignProjectTag/AssignProjectTagEndpoint.cs` — `PUT Projects/{projectShortName}/Tags/{tagName}`:
  - Drop auto-create. `TagName.TryCreate` → 400; `handler.FindTagAsync` → **404 if null**; `project.AssignExistingTag(tag)` → `.ToProblem()`; save; evict. Drop `IUserInfoQuery` dependency (no actor needed when not creating).
  - Update docs description ("Assigns an existing tag; 404 if no such tag.").
- **Modify** `UnassignProjectTag/UnassignProjectTagEndpoint.cs`:
  - `project.RemoveTag(...)` → `.ToProblem()` (409 on read-only); drop `IProjectTagHandler` dependency.
- **Modify** `UpdateProject/UpdateProjectEndpoint.cs`: replace inline `DomainTypeName` length/whitespace check with `DomainTypeName.TryCreate` → 400 (decision 5). (Leave `ProjectName`/`Description` manual checks as-is — out of scope; note as optional follow-up.)
- Register `MapCreateProjectTagEndpoint()` in `EndpointsExtensions.cs` (Project section).
- **Verify:** `dotnet build` green.

### Step 5 — RBAC (decision 1 delta)
File: `roleManagmentRBAC.json`.

- **Add** mapping: `POST /v2/Projects/{projectShortName}/Tags` → `perm:project:tag:create`.
- Keep existing `PUT`/`DELETE .../tags/{tagName}` → `perm:project:project:tag:assignment`.
- Bump `endpointMappings.count` 131 → **132**. Validate JSON.
- **Verify:** JSON parses; the three tag routes carry the right permissions (POST=create, PUT/DELETE=assignment).

### Step 6 — Docs
File: `docs/ddd-migration/legacy-to-refactored-endpoint-migration.md`.

- Tag rows: PUT no longer "creates if missing" → now "assign existing, **404 if absent**, idempotent"; **add** POST row "create-and-assign, requires `tag:create`". Update breaking-changes summary accordingly.
- CONTEXT.md + ADR-0007 already updated — no change.

### Step 7 — Tests
Folder: `tests/Modules/Core/Core.UnitTests/Projects/`.

- **New** `CreateProjectTagEndpointTests`: creates missing tag + assigns; idempotent re-create; 400 invalid name; 404 out-of-scope/project-missing; **409 when project read-only**.
- **Update** `AssignProjectTagEndpointTests`: now **404 when tag does not exist** (no auto-create); assigns existing; idempotent; 409 read-only.
- **Update** `UnassignProjectTagEndpointTests`: idempotent; **409 read-only**.
- `UpdateProjectEndpointTests`: confirm `DomainTypeName` invalid → 400 (now via `TryCreate`).
- **Verify (test-runner caveat):** run `dotnet test tests/Modules/Core/Core.UnitTests/<csproj>` with **no extra flags**; capture + `Select-String` to filter. Expect all tag/update tests green; 8 pre-existing `*DatabaseAccessTests` failures unchanged.

---

## Out of scope / not in this plan
- Slice C (resource assign/unassign) and Slice D (create/delete project, users, roles) — deferred per handoff.
- Catalogue tag soft-delete reactivation — no v2 catalogue-management surface yet; `HasTag`/`AssignTagAsync` now agree on `!IsDeleted`, the remaining interaction with a legacy-soft-deleted catalogue tag is documented, not built.
- Broadening `TryCreate` to `ProjectName`/`Description`/`Email` — optional consistency follow-up.

## Suggested final gate
`dotnet build` + `dotnet format --verify-no-changes` + `Core.UnitTests`, then two commits as above (commit 1 first, isolated).
