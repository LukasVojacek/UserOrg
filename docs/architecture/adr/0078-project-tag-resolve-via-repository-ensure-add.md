# 0078 — ProjectTag resolve-or-create moves behind the repository port; `IProjectTagHandler` is deleted

Date: 2026-07-14
Status: Accepted
Issue: #249 (P1.4)

## Context

`IProjectTagHandler` (`src/Modules/Core.Domain/Project/IProjectTagHandler.cs`) is the last surviving
domain-owned async-DB "handler" port from the pre-refactor era. Its sister ports
(`IProjectRoleHandler`, `IProjectResourceHandler`, `IProjectCreationHandler`) were already deleted —
their logic was folded into aggregates over loaded collections (`Project.UpsertMemberRole`). Tags
cannot be folded the same way: `ProjectTag` is a shared catalogue aggregate (M:N skip-navigation),
so the use-case genuinely needs an async resolve step before the synchronous
`Project.AssignExistingTag(tag)` mutation.

The sanctioned async seam for that already exists: `IRepository<ProjectTag, TagName>`
(port in `Core.Domain/Primitives/IRepository.cs`, adapter
`Core.DataAccess/Contexts/Repositories/DatabaseContext.ProjectTagRepository.cs`, DI at
`Core.DataAccess/ServiceCollectionExtensions.cs:82`, already consumed by `DeleteProjectTagEndpoint`).
The handoff's alternative — extending `IProjectTagQuery` — is rejected: `IDataQuery` is the untracked
read side; ensure/create is a write operation.

Two behavioural facts complicate a naive swap:

1. **Soft-delete semantics differ.** The handler's lookups ignore `IsDeleted`; the repository's
   `FindAsync` filters it (`!t.IsDeleted`). Today a soft-deleted tag is silently found and assigned.
2. **Shadow state on create.** `EnsureTagExistsAsync` sets the surrogate shadow PK (`Id`) and resolves
   the creator's `CreatedByUserId` shadow FK from `UserName`; the repository's plain `AddAsync` does not.
   The unique index on `ProjectTag.Name` is deferred (until legacy writers retire), so duplicate-name
   rows created now would block that index later.

## Decision

1. **Delete** `IProjectTagHandler`, `DatabaseContext.ProjectTagHandler.cs`, and the DI registration
   (`Core.DataAccess/ServiceCollectionExtensions.cs:81`).
2. **Endpoints use the repository port.**
   - `AssignProjectTagEndpoint`: `repository.FindAsync(tagName, ct)`; `null` (including soft-deleted)
     → `404 project.tag_not_found`.
   - `CreateProjectTagEndpoint`: `await repository.FindAsync(tagName, ct)
     ?? await repository.AddAsync(ProjectTag.Create(tagName, actor.UserName), ct)`.
3. **`AddAsync` gains ensure semantics on the natural key** (in the `ProjectTag` repository adapter,
   where the DbContext lives): if a row with the same `Name` exists it is necessarily soft-deleted
   (an active one would have been found by `FindAsync`) — the adapter **restores** it via the new
   aggregate method and returns the tracked existing aggregate instead of inserting a duplicate.
   On a genuine insert the adapter assigns the shadow `Id` (`Guid.CreateVersion7()`) and resolves
   `CreatedByUserId` from the aggregate's `CreatedBy` user name (missing creator remains a
   programming-error throw, as today — the endpoint 404s on a missing actor before this point).
4. **`ProjectTag.Restore()`** — a public, synchronous, idempotent aggregate method clearing the
   soft-delete flag (mirror of the existing `Delete()`).

## Consequences

- **Behaviour change (deliberate, an improvement):**
  - Assigning a soft-deleted tag now returns `404` (previously it silently assigned an invisible tag).
  - Creating a tag whose name matches a soft-deleted row now **revives** that row (previously it
    assigned the still-deleted row). No duplicate `Name` rows are ever created, keeping the data
    clean for the deferred unique index.
- Unit tests for both endpoints re-mock `IRepository<ProjectTag, TagName>` instead of the handler;
  new domain test covers `Restore()`.
- `CLAUDE.md`'s many-to-many pattern paragraph (which cites `IProjectTagHandler`) is rewritten to
  describe the repository-based resolve; `docs/ddd-migration/core-modules-target-architecture-and-plan.md`
  §8 flips P1.4 from ⚠️ to done.
- `Core.Domain` no longer contains any `*Handler` port — the migration invariant "async I/O in the
  use-case, sync mutation in the aggregate" is preserved (`AssignExistingTag` is untouched).
