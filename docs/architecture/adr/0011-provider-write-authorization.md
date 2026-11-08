# ADR 0011 - Provider/Location write authorization: claims-only, scoped by an additive providerName claim

- Date: 2026-06-09
- Status: accepted

## Decision

The modular Core provider/location write endpoints authorize **purely from the internal-token claims** — no database round-trip for authorization (consistent with ADR-0006). Two write actors:

- **Admin** — an OIDC user whose effective role claim (`caller.Role`) is `Admin`. May operate on **any** provider and is the **only** actor that may create a new top-level provider.
- **Provider** — the caller holding a `providerName` claim, by either authentication path: a provider service-key identity (API-key flow, identity type `provider`) or an interactive OIDC user whose effective role is `ProviderAdmin` and whose live provider assignment the Gateway resolved into the claim. May manage **only its own provider** — its details, privacy, country, (de/re)activation, and its locations. It may **not** create a provider.
- **ProviderAdmin is a tenant admin, scoped by its claim.** The role by itself authorizes nothing on the write path; the `providerName` claim matching the route's provider is what does. A `ProviderAdmin` therefore reaches exactly one provider — the one its claim names.
- **`CreateProvider` is the exception and stays `Admin`-only.** A `ProviderAdmin` holds no `providerName` claim for a provider that does not exist yet, so the create endpoint keeps its own role check instead of routing through the shared claim-scoped gate.
- Any other principal → `403`.

The two-authentication-path form of the **Provider** actor and the `CreateProvider` exception are decided in
[ADR-0105](0105-provider-write-authority-is-the-admin-role-or-the-callers-own-provider.md); the **Admin**
clause above is unchanged.

Ownership for the Provider actor is decided by comparing the **route's provider name** against a **`providerName` claim** on the internal token. The provider's natural key (`ProviderName`) is the identity used end to end — in the route, the repository key, and the claim — so the comparison is a string equality with no name↔surrogate bridging and no DB lookup.

To make that claim available, the **Gateway emits an additive `providerName` claim** when minting the internal token for a provider identity. The provider's surrogate id arrives as the vault key's `Data` field (currently a Guid, surfaced today as the `sub` and `provider_id` claims). The Gateway has **no reference to `Core.DataAccess`** and no Guid→name resolver exists yet, so we add one in **`Forge.Core`** (which the Gateway already depends on for permissions): a small `IProviderIdentityResolver` over a raw-SQL lookup on `UserOrgDbContext` (`select Name from Provider where Id = @id`, **without** the soft-delete filter so a deactivated provider can still reactivate), cached in `HybridCache` (the same shape as `UserPermissionsCache`). The existing `providerId` (Guid) claim is **kept unchanged** for its current consumers (the Request write path, legacy `TryGetProviderId`); the name claim is purely additive.

A second Gateway change is required: `AuthorizeRequestAsync` runs an endpoint-permission gate **before** Core. The new `v2` routes must be added to the endpoint-permission map (`roleManagmentRBAC.json`, mapped to `perm:provider:*`), or the gate returns `403` for everyone. The gate's API-key branch currently demands a per-key `perm:service:{name}` permission, which a generic provider-write route cannot enumerate; it is therefore extended with a provider branch — when the identity type is `provider` and the endpoint requires a `perm:provider:*` permission, the gate passes and lets Core perform the precise (claim-scoped) authorization.

The provider name is **not mutable**; update changes description, support email, privacy, and country only. All mutating routes carry `.DisabledWhen("ReadOnlyMode")`.

## Context

- Migrating the legacy provider/location write surface into the modular Core needed an authorization model, and the module had **no role-gated write endpoint** yet. Legacy gated provider writes on `httpContext.TryGetProviderId` — i.e. the provider service-key's id claim — so the real write actor was always a **provider service-key identity**, not an interactive OIDC "ProviderAdmin" user.
- Validation of the token-mint path settled which facts are trustworthy from claims alone:
  - **OIDC flow** (`OnTokenValidated` + the YARP transform) attaches `sub` / `preferred_username` / `email` / `role` / auth-method; the role comes from the permissions-cache snapshot. It **never** attaches a provider claim.
  - **API-key flow** (`ApiKeyAuthenticationHandler`) sets, for a provider key, `role = "Provider"` (the key *type*, not the `ProviderAdmin` system role), identity type `provider`, and a `providerId` (Guid) claim.
- So a provider's identity is fully established by the Gateway before the request reaches Core — re-deriving it from the database in Core would duplicate work the Gateway already did (the same reasoning as ADR-0006 for project scope).
- We are moving the model toward **`ProviderName` as the natural identity** (see the standing TODO on `User._assignedProviderId` about preferring `ProviderName` over the surrogate to avoid cross-aggregate coupling). Putting the **name** in the claim — rather than bridging a Guid claim to a name route — is the DDD-aligned choice and removes the surrogate from the auth boundary entirely.

## Considered options

- **Claims-only, additive `providerName` claim (chosen).** No DB on the auth path; name-based comparison matches the natural key and the read routes; existing Guid consumers untouched. Cost: a Gateway resolution (Guid→name, cached) and a new claim.
- **Claims-only, compare the Guid claim.** Rejected: forces either Guid-based write routes (diverging from name-based reads) or a Core name→Guid lookup, and keeps the surrogate on the auth boundary — against the natural-key direction.
- **Role from claim + provider assignment from the DB.** Rejected: the assignment is already knowable at the Gateway (the provider identity *is* the provider); a per-request Core lookup re-derives what the claim can carry.
- **Replace the Guid `providerId` claim with the name.** Rejected: breaking for the Request write path and legacy `TryGetProviderId`, which consume the Guid. The name claim is therefore additive.

## Consequences

- **Cross-component change.** This decision spans the **Gateway** (emit the additive `providerName` claim, resolve+cache Guid→name in `Forge.Core`, extend the permission gate, add `v2` endpoint-permission mappings), **Core** (`Caller` exposes `ProviderName`; handlers branch `Admin` / provider-identity / else), and the write endpoints. It is not a Core-only change.
- **"No DB on the auth path" means the *Core* auth path.** Core scoping is a claim string-equality with no Core query. The Gateway *does* hit the DB — it already does so for project roles (`UserPermissionsCache`), and the new `providerName` resolver follows that established pattern. The principle is "Core trusts the token", not "nobody queries the DB".
- **Name comparison is case-insensitive via canonicalization.** The claim/route equality relies on `ProviderName` being canonicalized (see [ADR-0012](0012-provider-location-name-canonicalization.md)); without it, a case mismatch between the DB-sourced claim and the route would wrongly `403` a legitimate owner.
- **Eventual consistency, as before (ADR-0006).** A change to a provider's name or a key's provider would be reflected on the next token mint, bounded by the Gateway's token/permission cache TTL.
- **Name uniqueness becomes load-bearing for auth, not just routing.** `Provider.Name` has no unique index and the migrations-overlay rule forbids adding one, so create-time uniqueness is enforced in application code (pre-check including soft-deleted rows → `409`).
- This is the **first role-gated write** in the modular Core and the reference pattern for future infra-scoped (non-project) writes: identity and scope come from the token; Core trusts them.
