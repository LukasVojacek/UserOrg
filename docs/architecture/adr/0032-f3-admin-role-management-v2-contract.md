# ADR 0032 — F3 admin role management: v2 contract for provider-admin promote / demote

- Date: 2026-06-28
- Status: accepted (2026-06-28) — approved at the F3 plan gate; live on `feat/refactor-core`.
- Relates to: ADR-0024 (v2 contracts: zero surrogate GUIDs), ADR-0029 (natural keys at contract & aggregate boundaries), ADR-0011 (provider write authorization / `providerName` claim), ADR-0018 (read-scope tiers), ADR-0027 (v2 natural-key uniqueness gaps — gap #2).

## Context

Legacy exposes two platform-admin operations: `PUT api/Administration/PromoteToProviderAdmin?IdpUserId=&ProviderId=`
and `PUT api/Administration/ResetToPlatformUser?IdpUserId=`. They mutate the **Core** `UserPermission` system row
(`ProjectId IS NULL`) and the user's provider link — role *assignment* lives in Core; Forge only owns the role
*catalogue* (definitions). So this is a Core write feature, not a Forge one.

The modular `User` aggregate **already implements** the domain behaviour: `PromoteToProviderAdmin(IReadOnlySet<ProviderId>)`,
`ResetToPlatformUser()`, `UnassignProvider(ProviderId)`, with guards (Admin role immutable; ProviderAdmin needs ≥1
provider; cannot demote while a provider is still assigned), domain events (all `IUserScopedEvent` → automatic output-cache
eviction), and repository role-map hydration on `FindAsync`. What is missing is the **HTTP/v2 surface**, error-code →
status mapping, and a provider-id resolver. F3 is that surface.

Three things must be decided to expose it cleanly: the contract shape, how the surrogate-free aggregate talks to a
GUID-typed method, and how the strict-demote invariant reconciles with a one-call "reset".

## Decision

1. **Provider-admin is a natural-key sub-resource of a user (ADR-0024/0029).**
   - `PUT  api/v2/Users/{idpUserId}/ProviderAdmin/{providerName}` — promote (idempotent: re-promoting to the same provider is 204).
   - `DELETE api/v2/Users/{idpUserId}/ProviderAdmin` — reset to platform user.
   No surrogate GUID crosses the contract: the user is addressed by its IdP subject (`idpUserId`), the provider by its
   natural key (`providerName`). Both documented in the **v2-Admin** OpenAPI document.

2. **Resolve `providerName → ProviderId` in the use-case; keep the aggregate's GUID signature (defer the rename).**
   `User.PromoteToProviderAdmin` takes `IReadOnlySet<ProviderId>`, but `Provider` exposes only its natural key `Name`
   (surrogate `Id` is an EF shadow). Rather than refactor the aggregate to accept `ProviderName` now (the `User.cs`
   TODO — a schema-touching change toward a 1:N junction), the use-case resolves the id via a read query
   (`IProviderQuery.FindActiveProviderIdByNameAsync`, the existing `EF.Property<ProviderId>(prov,"Id")` pattern) and
   passes the resolved `ProviderId` into the synchronous aggregate method. This is the established resolve-in-use-case
   pattern (ADR-0030/0031): async resolution outside, sync invariant inside.

3. **Reset orchestrates unassign-then-demote in the use-case (one transaction).**
   `ResetToPlatformUser()` refuses while a provider is still assigned, and promote always assigns one — so a literal
   one-call reset is impossible without first unassigning. The DELETE use-case calls `UnassignProvider` for each
   `user.AssignedProviderIds`, then `ResetToPlatformUser()`, in a single `SaveChanges`. This matches the legacy
   one-step semantics while leaving the strict aggregate invariant (no orphan provider-admin) intact. Two events fire
   (unassign + demote); both are `IUserScopedEvent`, so cache eviction is idempotent.

4. **Admin-only, deny-by-default RBAC.** Both routes carry an in-endpoint `caller.Role == Admin` check (defence in
   depth) and a `roleManagmentRBAC.json` mapping to `perm:platformadmin` (the Gateway is deny-by-default; an unmapped
   route is 403). The Admin role already holds `perm:platformadmin`.

5. **Add user error codes + HTTP mapping.** `UserErrors` build without a `code`, so `ToProblem()` maps them all to 400.
   F3 adds `UserErrorCodes` and wires `ToProblem()`: AdminRoleImmutable → 409, ProviderAdminRequiresProvider → 422,
   CannotDemoteWithProviders → 409, AlreadyAssignedToProvider → 409, NotAssignedToProvider → 404.

6. **Legacy v1 stays; no DB unique index.** Per the cutoff strategy, legacy promote/demote remain until the F7 parity
   gate. Because two write paths coexist, F3 adds **no** unique index for the system role — the aggregate's single
   system-permission rule is the only enforcement. The aggregate keeps exactly one system permission per user and
   never adds a second row (only `PromoteUserToRole` creates one, on a new aggregate). Promote and reset change that
   row in place through `UserPermission.ChangeRole`, and `SystemRoleName` (`.Single()`) fails fast if v1 left zero
   or several rows. Nothing is soft-deleted, because the `SystemRoleOnly` filter would still load such a row
   (ADR-0115). The ADR-0027 **gap #2**
   project-role dedup + filtered unique `(UserId, ProjectId)` index is explicitly **out of F3 scope**.

## Consequences

**Benefits**
- The v2 contract is GUID-free and RESTful; provider-admin reads as what it is — a state of a user scoped to a provider.
- No schema migration and no change to the already-tested `User` domain methods; F3 is additive (query + endpoints + codes + RBAC + tests).
- Error responses become semantically correct (409/422/404) instead of a blanket 400.

**Drawbacks / risks**
- Two coexisting write paths (v1 + v2) can each still create state; the DB cannot enforce single-system-role until v1 is decommissioned — only the aggregate does, and only on the v2 path.
- Reset firing two events (unassign + demote) is a small divergence from the legacy single mutation; harmless (cache eviction idempotent) but worth knowing when reading telemetry.
- The `ProviderId`-typed aggregate method + use-case resolution is a deliberate deferral; the `ProviderName` refactor TODO remains until the 1:N provider-junction migration.

## Alternatives considered

- **Single `PUT users/{idpUserId}/system-role` with a body** — rejected: less RESTful, mixes promote/reset, and re-introduces a role/provider payload where natural-key routing is clearer.
- **Atomic `ResetToPlatformUser` that also clears the provider** — rejected for F3: changes already-shipped, tested domain behaviour for no contract benefit (use-case orchestration achieves the same one-call UX).
- **Decommission legacy in F3 to allow the unique index** — rejected: violates "v1 lives until the cutoff" and risks breaking callers still on v1; the index waits for v1 retirement.
