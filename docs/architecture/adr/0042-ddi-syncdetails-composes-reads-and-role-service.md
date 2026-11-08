# ADR 0042 - DDI SyncDetails composes Core.DataAccess reads with Forge.Core role resolution (faithful parity)

- Date: 2026-06-29
- Status: accepted
- Relates to: [ADR-0017](0017-ddi-service-api-as-separate-module.md) (the DDI module), [ADR-0018](0018-read-scope-tiers.md) (read-scope precedent). Extends ADR-0017's DDI dependency contract.
- Tracking: [issue #224](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/224) (all-users exposure concern).

## Context

F6/D5 ports legacy `GET /api/Service/SyncDetails` (provider-scoped, provider API key) into the DDI
module. The legacy `AdministrationService.GetSyncDetails(providerId)` (`src/Core/Application/UserOrg/Services/AdministrationService.cs:19`)
returns `List<UserInfoExtendedModel>` and is **not a pure DB read**. It composes:

1. the provider's projects + their assigned (non-deleted) users (DB);
2. each user's `UserPermission` rows → `(ProjectShortName → roleId)` (DB);
3. role-id → **role name + permissions** via `IRoleListService.ListRoles` (Forge.Core — a DB-backed,
   cached local query, **not** an external HTTP service);
4. each project's resources (`ProjectResourceDetailModel`) for the per-project role detail.

Two facts settled the open questions the F6 handoff raised:

- **The Role Service exists in v2.** `Forge.Core.Features.Role.Services.IRoleListService` is already
  consumed by modular Core endpoints (`GetValidRoles`, `ChangeProjectUserRole`) — D5 is **not**
  Role-Service-blocked.
- **SyncDetails is live, not Gateway-served.** No Gateway forwarding exists for it (unlike F2
  CyclopsUsage); it is in `roleManagmentRBAC.json` + the published API docs, and the parity tracker
  marks it **must-port (B3→DDI)**.

The DDI module today references only `Core.DataAccess`, `UserOrg.Shared`, and the Vault contracts — it
does **not** reference Forge.Core.

## Decision

D5 implements SyncDetails as a **use-case composition in the DDI module**, mirroring the established
read/port split — the read query stays a pure DB projection; the endpoint composes:

- A new read query in **`Core.DataAccess`** (`ISyncDetailsQuery`, mirroring `IDdiProjectsInfoQuery`)
  returns the raw rows: provider's projects + assigned users, *all* users, per-user
  `(ProjectShortName → roleId)` permissions, and per-project resources. **No role names, no role
  service** in the query — it only emits role **ids** and DB scalars. LINQ **method-syntax** (modular
  read-query rule), staged + assembled in memory like `DdiProjectsInfoQuery` (shadow FKs span more sets
  than one join supports).
- The **DDI endpoint** (`GetSyncDetailsEndpoint` in `Features/Service`) resolves the provider via
  `HttpContext.TryGetProviderId` (**400** if absent), calls `IRoleListService.ListRoles` once, and maps
  role-ids → names/permissions in memory, producing the `UserInfoExtendedModel` wire shape.
- The DDI project gains a **`ProjectReference` to Forge.Core** so it can inject `IRoleListService`.
  Forge.Core's role services are already registered in the host; D5 confirms the registration covers the
  DDI endpoints' enumeration test (the `DdiModuleEndpointsTests` host-coupling gotcha).
- **Response = faithful legacy parity (both groups):** (1) provider's users with `SystemRoles` filtered
  to the provider's projects, **plus** (2) every other platform user with their global roles
  (`filterProjects: null`). This preserves the external sync consumer's contract and avoids a v1↔v2
  divergence before the F7 cutoff gate.

## Consequences

- **The DDI module now depends on Forge.Core**, not only Core.DataAccess + Vault. This is a deliberate,
  recorded extension of ADR-0017's DDI dependency contract: role resolution is a cross-cutting Forge
  concern, and the new modular Core does not (and need not) own a role aggregate. The read query stays
  persistence-pure; only the use-case crosses to Forge.
- **Faithful parity carries a known over-exposure.** Group (2) hands a single provider the full platform
  user roster (email, IDP sub, system roles). This is intentional for parity but is **tracked in
  [issue #224](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/224)** for a
  data-minimisation review **after** the cutoff — it is not silently accepted as the v2 end state.
- **No new persistence unit, no migration.** D5 is read-only over existing tables (modular-migrations
  freeze respected). The `ISyncDetailsQuery` is the heaviest DDI read (users + permissions + roles +
  resources); some sub-reads may not yet exist as modular query objects and are added under
  `Core.DataAccess`.
- **Role emission is driven by live `UserPermission` rows, not a membership re-check.** Legacy gated each
  role entry on the user being a live member/creator of the project; v2 emits from the role rows directly.
  These cannot diverge through v2 writes — membership and the project-role row are coupled as one unit
  (ADR-0008: `AssignUserWithRole` / `UnassignUserWithRole` / `Delete` create and soft-delete them together),
  so a live role never outlives its membership. The only way to produce drift is inconsistent legacy-v1
  role/membership state, which disappears at the v1 cutoff; the F7 real-SQL parity test is the backstop.
- **Real-DB shape parity is the verification point.** In-memory EF tests cannot catch SQL-translation
  regressions (cf. the `ORDER BY <VO>.Value` 500s); D5's query needs a real-SQL guard test, and
  feature-level verification confirms v1↔v2 response-shape parity (developer/CI, `USERORG_TEST_DB`).

## Considered options

- **Inject `IRoleListService` into the Core.DataAccess query (rejected).** Would keep the endpoint thin
  but pollutes the read-DTO/query seam with a Forge service dependency, breaking the "queries are pure DB
  projections" rule. Composition belongs in the use-case.
- **Reuse legacy `AdministrationService` / `UserService.PopulateExtendedUserInfo` (rejected).** That is
  legacy `src/Core/Application` code; D5's purpose is to re-home the behaviour into the module, not call
  back into the monolith.
- **Provider-scoped response only — drop group (2) (deferred, not chosen).** Tighter and lower-exposure,
  but a parity divergence to be decided post-cutoff with the sync consumer — captured in issue #224.
- **DDI use-case composition + Forge.Core reference (chosen).** Preserves the read/port split, reuses the
  existing role service, records the one new module dependency here.
