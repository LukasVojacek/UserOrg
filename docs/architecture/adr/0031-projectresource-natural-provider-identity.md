# ADR 0031 — ProjectResource carries its owning provider as a shadow FK with a ProviderName projection

- Date: 2026-06-28
- Status: accepted (2026-06-28) — F5 Phase A prerequisite; live on `feat/refactor-core`.
- Supersedes / amends: **ADR-0030 §2** (nullable CLR `ProviderId` → nullable **shadow FK** + `ProviderName` projection) and **ADR-0030 §4** (the create-context read query is replaced by an aggregate load + a NameTaken-only pre-check).
- Relates to: ADR-0026 (per-provider resource identity), ADR-0027 (natural-key uniqueness gaps), ADR-0029 (natural keys at contract & aggregate boundaries), ADR-0030 (ProjectResource write model).

## Context

ADR-0030 §2 left the owning provider on `ProjectResource` as a **public CLR property** `ProviderId? ProviderId` —
a raw surrogate `Guid` on the domain surface. ADR-0029 says the surrogate id is a storage concern confined to the
schema (§3); a natural key is what crosses the aggregate boundary (§4). The exposed `ProviderId` violates that: it
is a `Guid` that never crosses a contract (no read returns it, no write reads it — the write side resolves the
owner by `ProviderName`), yet it sits on the model where any caller can take it.

F5 (resource↔project assignment) is the moment to clean this up before more code reads `resource.ProviderId`. The
fix mirrors the already-shipped `Project._projectDomainType` → `DomainType` pattern: the surrogate lives in storage,
the natural key is the surface.

## Decision

1. **Owning provider as a private navigation + shadow FK.** `ProjectResource` drops the public CLR `ProviderId` and
   gains a `private Provider? _provider` backing field. EF maps the relationship `HasOne<Provider>("_provider")
   .WithMany().HasForeignKey("ProviderId")` onto a **shadow FK property** `"ProviderId"` (nullable
   `ProviderId?`, column `ProviderId uniqueidentifier`). **No schema change** — the physical
   `FK_ProjectResource_Provider_ProviderId` and `IX_ProjectResource_ProviderId_Name` are preserved (the index now
   declared over the shadow `ProviderId` + `ResourceName`).
2. **`ProviderName` projection.** The domain surface exposes only
   `public ProviderName? ProviderName => _provider?.Name` — null only for legacy standalone rows. The surrogate
   never leaves storage (ADR-0029 §3/§4).
3. **`Create` takes a `Provider`.** `ProjectResource.Create(resourceName, Provider provider, …)` sets `_provider`;
   EF derives the shadow FK from the navigation. The factory no longer accepts a raw `ProviderId`.
4. **Repository load includes `_provider`** so the natural identity is whole after materialisation
   (`FindAsync` → `.Include("_provider")`).
5. **Create use-case reworked (supersedes ADR-0030 §4).** Because `Create` now needs the `Provider` entity (the FK
   principal) and the surrogate id must not surface, the create-context read query `GetResourceCreateContextAsync`
   (which returned `(ProviderId, NameTaken)`) is removed. The use-case instead:
   - loads the owning provider via `IProviderRepository.FindAsync(providerName)` → 404 if missing/deactivated (the
     loaded aggregate is the FK principal — its surrogate id stays internal to EF);
   - calls a narrowed `IResourceQuery.IsResourceNameTakenAsync(providerName, resourceName)` → 409 (per-provider
     uniqueness pre-check; the filtered unique index remains the concurrency backstop).
6. **Provider-name active-set uniqueness (ADR-0027 item).** A filtered unique index `IX_Provider_Name WHERE
   IsDeleted = 0` is added (legacy migration `AddProviderNameUniqueIndex`, mirrored in the modular
   `ProviderConfiguration`). The active set is already unique per ADR-0027, so it is additive (no dedup). The
   create-provider 409 guard (`ProviderExistsAsync`) already existed and is unchanged.

## Considered options

- **Denormalised `ProviderName` column on `ProjectResource`.** Rejected — a schema migration + backfill rewriting
  week-old ADR-0026 schema, for a value the FK already determines and no write flow reads as a column.
- **Public `Provider` navigation.** Rejected — embeds the aggregate root in another aggregate; the projection
  exposes only the natural key.
- **EF alternate key on `Provider.Name`.** Not needed — the `ProjectResource → Provider` FK targets the
  `Provider.Id` PK (the existing physical FK), so no alternate principal key is required. (The plan listed it as a
  possibility; it added nothing here and was skipped.)

## Consequences

- `ProjectResource` is ADR-0029-clean: no surrogate on the domain surface; the owner reads as `ProviderName`.
- F5 Phase B–E are unaffected by this change — they address the resource by `ResourceName` and never need the owner
  surrogate. Phase A is an independent, separately-committed refactor of just-closed F4 code, kept bisectable.
- **Migrations-overlay snapshot stays frozen** — Phase A adds exactly one real legacy migration
  (`IX_Provider_Name`); the modular snapshot drift is pre-existing overlay debt (ADR-0030 Consequences), not F5.
- A NULL-`ProviderId` legacy row still has no `ProviderName` (null projection) and remains unreachable via the
  provider-nested routes — unchanged from ADR-0030 D5.
