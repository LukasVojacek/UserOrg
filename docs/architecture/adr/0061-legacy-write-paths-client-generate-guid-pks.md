# ADR 0061 - Legacy `DatabaseContext` write-paths client-generate Guid PKs (shared UUID v7 generator)

- Date: 2026-07-12
- Status: accepted
- Relates to: [ADR-0049](0049-modular-canonical-migration-cutover-by-id-identity.md) (modular `Core.DataAccess` is the canonical migration owner; its schema dropped `DEFAULT NewId()`), [ADR-0047](0047-new-modular-tables-ship-via-legacy-migration.md). Completes the legacy-side half of the ID-identity cutover. Issue #246.

## Amendment (2026-08-12) — §5 no longer holds for the HEAppE modular configs

The `merge main → feat/refactor-core` round 2 (see [ADR-0091](0091-merge-main-repair-squash-lineage-round-2.md), decision 5 of `plan-ready.md`) accepted `main`'s HEAppE migration
`20260728131206_DropGuidIdDefaults`, which drops the `DEFAULT NewId()` constraint from the
HEAppE modular Guid PK columns §5 below names (6 as of this merge, not the 5 §5 counts — a
pre-existing discrepancy, not corrected here). Those 6 `IEntityTypeConfiguration` classes
(`src/Modules/Integrations/Heappe/Persistence/Configurations/{CommandTemplateDefinition,
CommandTemplateLocationDefinition, CommandTemplateParameterDefinition, LocationPartition,
ProjectCommandTemplate, ProjectCommandTemplatePartition}Configuration.cs`) now set
`.ValueGeneratedNever()` and rely on ctor-side `Guid.CreateVersion7()` minting — `main`'s
pattern, not this ADR's shared `ApplyCodeGeneratedGuidKeys()` convention. **§5's claim that
these configs "still use" `DbConstants.GuidIdSql` is false as of this merge**; a repo-wide grep
confirms no reference to it remains outside `DbConstants.cs` itself. This is recorded here
rather than silently edited into §5 below, which is left as written for the historical record of
the 2026-07-12 decision.

## Context

The modular-canonical schema — owned by `Core.DataAccess.Contexts.DatabaseContext` and shipped in
`20260630191024_ModularCleanup` — **dropped the `DEFAULT NewId()` constraint** from every Guid PK column
(and from the `User.IdentityProviderUserId` string column, and it drops the dead `Configuration` table).
The modular side compensates in code: `AuditableEntityConventions.ApplyCodeGeneratedGuidKeys` stamps each
single-Guid PK with a UUID v7 at `Add` time via an `internal` `UuidV7ValueGenerator`
(`src/Modules/Core.DataAccess/Conventions/`), so EF sends the `Id` explicitly.

The **legacy** `UserOrganizationManagement.Infrastructure.DataAccess.Contexts.DatabaseContext`
(`src/Core/Infrastructure.DataAccess`) — still the runtime context for ~20 repositories, `UserOrgUnitOfWork`,
self-onboarding and `GET /api/UserInfo` — never got that treatment. Its 14 EntityConfigurations still map the
PK as store-generated:

```csharp
builder.Property(x => x.Id).HasDefaultValueSql(DbConstants.GuidIdSql); // "NewId()"
```

When `Id == Guid.Empty`, EF **omits** the column from the INSERT and waits for a DB default. On the
modular-canonical schema that default no longer exists → `INSERT NULL` → **`SqlException 515: Cannot insert
the value NULL into column 'Id'`**. The user-visible symptom: the first login of an unknown identity
(`GET /api/UserInfo`) returns 500 and no `dbo.User` row is created. The E2E suite currently papers over this
with test-only `DF_*_E2E DEFAULT NEWID()` constraints in `seed-data.sql`.

The legacy `DatabaseContext` owns **zero migrations** and is in neither list of the `DatabaseMigrations`
tool (ADR-0049 moved ownership to the modular context). So this is **not** a migration change — the DB is
already correct. The remaining defect is purely the legacy EF configuration still describing a store-generated
key.

## Decision

Legacy write-paths generate the Guid PK **client-side**, identically to the modular side, by sharing one
generator and one convention:

1. **Relocate** `UuidV7ValueGenerator` (unchanged: `Guid.CreateVersion7()`, maps through the property's
   value converter so typed ids like `ProjectId` work) into `UserOrg.Shared.Persistance` — the persistence
   home already referenced by **both** contexts. It stays `internal`; a **public**
   `ModelBuilderExtensions.ApplyCodeGeneratedGuidKeys()` extension in the same assembly is the seam both
   contexts call. Modular `Core.DataAccess` deletes its local copy and calls the shared extension — no
   behavior change (identical logic).
2. The legacy `DatabaseContext.OnModelCreating` calls `modelBuilder.ApplyCodeGeneratedGuidKeys();` after
   `ApplyConfigurationsFromAssembly`, mirroring the existing `ApplyAuditableEntityConfiguration()` calls.
   The convention only touches **single Guid PKs** (int-identity lookups and composite-key join entities are
   skipped), so all 14 aggregates are covered with one line.
3. **Remove** `HasDefaultValueSql(DbConstants.GuidIdSql)` from the 14 legacy configs (now dead — the
   convention sets `HasDefaultValueSql(null)` + the value generator). Also remove the dead
   `HasDefaultValueSql(DbConstants.GuidIdSql)` on the `User.IdentityProviderUserId` **string** column (the
   IdP always supplies that value; the default never fired and the underlying DB default is already gone).
4. **Remove** the test-only `DF_*_E2E` DEFAULT workarounds from `tests/E2E/E2E.Fixtures/seed-data.sql`, so the
   E2E suite proves the real fix rather than masking it.
5. **Keep** `DbConstants.GuidIdSql` — the HEAppE modular integration configs
   (`src/Modules/Integrations/Heappe/...`, 5 files) still use it. *(No longer true — see the
   2026-08-12 amendment above.)*

## Consequences

- `GET /api/UserInfo` (and every legacy insert) sends an explicit UUID v7 `Id`, independent of any DB
  default. **Backward-compatible with `main`'s legacy schema too** (an explicit `Id` INSERT succeeds whether
  or not the column carries a `DEFAULT`), so the shared legacy configs converge rather than diverge — this is
  the coordinated cutover, not an isolated hotfix.
- One generator, one convention, both contexts. No new legacy-Core → Modules dependency (the shared home is
  `UserOrg.Shared`, already on both reference chains).
- **Accepted risk:** the convention now stamps *any* single-Guid-PK legacy entity, including any not among the
  14. This matches the modular convention's existing behavior over the same aggregates; join/lookup keys are
  skipped by construction, and the F7 parity suite + E2E guard against regressions. A Guid PK that is
  deliberately app-assigned would still work (the generator only fires when the value is unset).
- No migration is created; the legacy context remains migration-less by design (ADR-0049).
- `Tests/Modules/Integrations/Waldur.Adapter.Tests/WaldurGuidStampingIntegrationTests.cs` pins this
  convention against a real SQL Server for the Waldur twin-creation write paths. It is opt-in
  (`USERORG_TEST_DB`, unset in CI) — see the class-level doc comment there for how and when to run
  it, and for the honest scope: unrun, it is not coverage, only a manual guard.
