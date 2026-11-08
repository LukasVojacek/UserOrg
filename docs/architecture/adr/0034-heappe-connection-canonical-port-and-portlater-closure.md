---
status: accepted
---

# HEAppE connection: canonical port + closure of ADR 0033 port-later list

## Context

[ADR 0033](0033-merge-main-adopt-waldur-efp-layout.md) merged `origin/main` into
`feat/refactor-core` and deferred six "port-later" items. A grounded re-read of the
merged tree (commit `dbd0286f`) shows that adopting main's **modular** Heappe code
resolved most of them at merge time — the deferred work is far smaller than the list
implies. This ADR records the canonical decisions and closes the list.

### What the merge already gave us (evidence)

Two HEAppE `TemplatesInfo` paths are live side-by-side:

- **Legacy v1** — `GET /api/Project/TemplatesInfo/{projectShortName}`
  (`src/Core/API/API/ProjectEndpoint.cs:347` → legacy `ProjectService.GetListAvailableClusters`).
  Took main's liveness-gate / fast-fail fix during the merge.
- **Modular v2** — `GetProjectTemplatesEndpoint`
  (`src/Modules/Core/Features/Project/GetProjectTemplates/`). Arrived from main already
  carrying the same behaviour.

The v2 `ProcessUrlAsync` already performs, per distinct URL:
1. the liveness gate `IsHostAliveAsync` fast-fail (`GetProjectTemplatesEndpoint.ProcessUrlAsync`),
2. one unpruned catalog fetch `GetClusterCatalogAsync` (`GetProjectTemplatesEndpoint.FetchCatalogAsync`), and
3. caller-side pruning against the project's `allowed` partition set
   (`GetProjectTemplatesEndpoint.AllowedCommandTemplates`, `allowed.Count == 0 || allowed.Contains(...)`).

And the v2 unit tests already assert exactly the port-later concerns:
- `Should_SkipExpensiveCall_When_HostIsNotAlive` (item 1 liveness gate),
- `Should_KeepOnlyAllowedCommandTemplates_When_AllowedSetIsNonEmpty` and
  `Should_KeepAllCommandTemplates_When_AllowedSetIsEmpty` (item 2 pruning, against
  main's **caller-provided** `allowed` signature — precisely the coverage 0033 asked
  to re-establish after the old `GetListAvailableClustersPruningTests` canary was dropped).

(`Tests/Modules/Core/Core.UnitTests/ProjectTemplates/GetProjectTemplatesEndpointTests.cs`.)

### The two `IHeappeConnectionService` interfaces

| Interface | Shape | Consumers |
|---|---|---|
| `Heappe.Contracts.IHeappeConnectionService` (main, **lean**) | `GetClusterCatalogAsync` (unpruned, per-URL cached) + `IsHostAliveAsync` + bare-set `GetActiveCommandTemplatesAsync(Guid)` | modular v2 `GetProjectTemplatesEndpoint` |
| `UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService` (legacy, **fat**, `Result`-returning) | `GetListAvailableClusters(...)` + `DownloadChangedFilesAsync` + `Result`-returning `GetActiveCommandTemplatesAsync` | legacy Core only — `ProjectService`, legacy `HeappeEndpoint`, legacy `Userorg.Adapter` |

The legacy interface's only three `using IHeappeConnectionService =` alias sites
(`ProjectService.cs`, `Userorg.Adapter/Heappe/HeappeConnectionService.cs`,
`Tests/Core/Application.Tests/ProjectServiceTests.cs`) are all inside legacy Core.

## Decision

1. **Canonical port = main's lean `Heappe.Contracts.IHeappeConnectionService`.**
   It is the migration target, it is consumed by the live v2 endpoint, and its lean
   shape (catalog + liveness + bare-set) is the contract new modular features bind to.
   (Closes port-later item 3.)

2. **The legacy fat interface is retired *with* legacy Core, not now.** It is consumed
   only by legacy-Core code that is being migrated feature-by-feature. No premature
   deletion and no touching the three `using`-alias sites — they disappear when the
   legacy `TemplatesInfo` and Heappe file-download endpoints finish migrating. Its
   unique methods already exist on the modular `IHeappeService`
   (`GetListAvailableClusters`, `DownloadChangedFilesAsync`), so those endpoints bind
   to modular contracts directly when they move.

3. **`GetActiveCommandTemplatesAsync` unwrap-and-throw is the deliberate contract**
   for the lean interface. The method's return is a bare `HashSet<...>`; the modular
   implementation surfaces an `IHeappeService` failure as an exception rather than
   masquerading a DB error as "no partitioning configured"
   (`HeappeConnectionService.cs:29-35`). This is the documented behaviour and stands.
   (Closes port-later item 4.)

4. **Domain vocabulary: keep the curated terms; main's request-state vocabulary is not
   adopted.** Root `CONTEXT.md` already deprecates `ProjectResourceAssignment`, and
   main's `PendingForPIAccept` / `PendingForHPCProject` / `PI` / `ProjectResource*`
   terms were intentionally not merged. No change. (Closes port-later item 6.)

5. **Items 1 and 2 are already satisfied** by the merged modular code and its tests
   (see Context). No production or test work remains for them; they are recorded here
   as closed with the evidence above.

6. **ADR numbering (item 5) is the only mechanical follow-up**, and it is optional
   docs hygiene — see Consequences. It is not a blocker.

## Consequences

- A clean-context reader now has a single recorded answer for which Heappe connection
  port is canonical and why the legacy one still exists. No code moves.
- Port-later items 1, 2, 3, 4, 6 are **closed**. The only open item is the cosmetic
  ADR-numbering cleanup.
- **ADR-numbering cleanup (item 5), recommended scope if done:**
  - Within `docs/architecture/adr/`, four numbers are duplicated by main's incomers.
    Renumber the *incoming* files to the next free block, leaving our established
    sequence intact and fixing each file's `#` title + any inbound references:
    - `0006-single-main-trunk-model` → `0035`
    - `0007-tagless-rc-track-manual-primary-release` → `0036`
    - `0008-per-track-runtime-base-images` → `0037`
    - `0030-templatesinfo-v2-catalog-once-per-url` → `0038`
  - **Leave the separate `docs/adr/` tree alone.** It is main's *behavioural* ADR set
    (0001–0004) and is referenced by code comments (e.g. the modular Heappe code's
    "ADR 0073 / ADR 0074"). Renaming it breaks those references for no benefit. Document
    the two-tree convention (`docs/architecture/adr/` = architecture decisions,
    `docs/adr/` = behavioural/runtime decisions) in a one-paragraph note instead of
    merging them.
