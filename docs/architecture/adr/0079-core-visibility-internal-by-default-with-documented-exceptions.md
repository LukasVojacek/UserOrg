# 0079 — `Core` visibility: internal by default, with a frozen, documented public surface

Date: 2026-07-14
Status: Accepted
Issue: #249 (P2.3)

## Context

The migration target (P2.3) is "everything in `src/Modules/Core` `internal` except
`AddCore`/`MapCoreEndpoints`". A code-verified audit found 17 public type declarations. Three facts
refine the original goal:

1. `Core` has `InternalsVisibleTo("Core.UnitTests")` + `DynamicProxyGenAssembly2`
   (`src/Modules/Core/AssemblyInfo.cs`), so narrowing breaks no tests.
2. Two types the handoff wanted narrowed **cannot** be: `IResourceSyncRunner` is implemented and
   registered by `Userorg.Adapter/Heappe` (`HeappeResourceSyncRunner.cs:18`,
   `HeappeAdapterExtensions.cs:41`); `ConditionalGetApplicationBuilderExtensions.UseCoreConditionalGet`
   is called by the host (`src/Core/API/Program.cs:150`).
3. The handoff's "keep Response DTOs public (OpenAPI needs it)" premise is false — the codebase-wide
   convention is `internal sealed record Response` nested in the endpoint class, and OpenAPI
   generation works with it. The 7 public UserInfo response records are the inconsistency, not the rule.

## Decision

1. **Narrow to `internal` (12 types):** `PageRequest`, `PaginationOptions`, `PagedResponse`
   (`Shared/Paging/`), `Caller` (`Shared/Auth/`), the Workflow `ServiceCollectionExtensions`
   (`Features/Resource/Workflow/` — called only from `AddCore`), and all 7 UserInfo response records
   (`UserInfoResponse`, `UserSystemRoleRecord`, `UserInfoExtendedResponse`,
   `UserSystemRoleExtendedRecord`, `ProjectResourceSummary`, `ClusterInfo`, `ClusterAttribute`).
2. **The frozen public surface of `Core` is exactly:**
   - `Composition/`: `ServiceCollectionExtensions` (`AddCore`), `EndpointsExtensions`
     (`MapCoreEndpoints`), `ApiAudience` — the composition seam the host calls.

     > **Note 2026-10-01.** `EndpointsExtensions` also exposes `public const string V2RoutePrefix`, read by
     > the host's `V2CoreExtensions.IsV2`. No new public type.
   - `Shared/Http/ConditionalGetApplicationBuilderExtensions` — a host-called middleware seam;
     functionally part of composition, kept in place.
   - `Features/Provider/ResourceAssignment/IResourceSyncRunner` — a port implemented by the
     `Userorg.Adapter` assembly; it disappears only when the durable-outbox decoupling (pending on
     its own branch) deletes it. Not this task's scope.
3. **The surface is enforced, not just grepped:** a hand-rolled reflection test in
   `Core.ArchitectureTests` (consistent with the existing no-NetArchTest style) asserts that the
   public types of the `Core` assembly equal exactly the allowlist above. Any future public leak
   fails `dotnet test`.

## Consequences

- Other assemblies can no longer bind to `Core` internals; the compiler becomes the boundary.
- OpenAPI documents must be verified unchanged after narrowing (build regenerates the tracked
  `API_*.json`; their diff must be empty).
- The allowlist test makes P2.3 permanent — future exceptions require editing the test and this ADR.
- `docs/ddd-migration/core-modules-target-architecture-and-plan.md` §8 flips P2.3 from ⚠️ to done,
  with the two documented exceptions noted.
