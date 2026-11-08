# ADR 0105 - Provider write authority is the Admin role or the caller's own provider

- Date: 2026-09-12
- Status: accepted

## Decision

A provider write is authorized by exactly two facts, in this order:

1. the caller's effective role claim is `Admin` — it may write **any** provider; or
2. the caller's `provider_name` claim equals the provider in the route — it may write **that** provider.

Nothing else is consulted. In particular the authentication method (`am`) and the derived
`Caller.IsServiceKeyIdentity` **leave the write gate entirely**: an interactive OIDC `ProviderAdmin` and a
provider service key are two login paths onto one actor and carry identical **write authority** behind
`ProviderWriteAuthorization`. `ProviderAdmin` is a tenant admin, and the claim — not the role name — is
what bounds it to one tenant.

This supersedes **only** two parts of
[ADR-0094](0094-interactive-provider-admin-identity-and-the-write-asymmetry.md): its
**"Writes stay service-key-only"** section (§34-38 as originally filed, D9) and its rejected alternative
**"Key both reads and writes off `am`"**. ADR-0094 keeps `- Status: accepted` and the rest of it stays in
force: **D5** (the interactive mint), **D10**, **D11**, the self-asserted-claim ingress strip and the
fail-closed rule.

**Write authority, not authorization generally.** Reads still differ by authentication path.
`src/Modules/Core/Shared/Auth/ReadScope.cs:57` keys by-natural-key read reach on
`caller.IsServiceKeyIdentity` (ADR-0094 D10) and is unchanged, live code: a service key resolves any
active record by name, an interactive provider identity only inside its own provider. This ADR narrows
its claim to the write path deliberately.

**`CreateProvider` is an explicit exception and stays `Admin`-only.** A `ProviderAdmin` holds no
`provider_name` claim for a provider that does not exist yet, so rule 2 has nothing to compare.
`POST /api/v2/Providers` therefore keeps its own `Admin` role check rather than routing through the shared
gate.

## Context

### Why the asymmetry existed, and why it is abandoned

ADR-0094's D9 was a write-only extra question laid on top of an already-resolved provider **read** tier:
the caller had landed in `ReadScope.Provider` on the strength of its `provider_name` claim, and the write
gate then asked a second, different question — *how* did you authenticate. That deliberately split read
reach from write reach for an inbound OIDC `ProviderAdmin`, as a conservative first step when the
interactive provider identity was new.

The split is abandoned because the interactive `ProviderAdmin` **is** a tenant admin: the role exists in
order to carry authority over its own provider, and a role that can read its tenant but write nothing is
not the role the product means. One actor, two login paths, one write authority.

### It restores ADR-0018 rather than weakening it

[ADR-0018](0018-read-scope-tiers.md)'s rule is that reads and writes must use the **same** key
(`0018-read-scope-tiers.md:11,14`, where the tier precedence "mirrors
`ProviderWriteAuthorization.Authorize` exactly"). D9 put a second key on the write side only. Removing it
leaves exactly one question on the write path — does `provider_name` match the route — which is the same
key reads use, so the two paths agree again and more literally than under D9. ADR-0018's only stated
dependency is the claim plus a role carrying `*:list` (`:46`), never `am`.

### The regression this closes

Introduced by commit `ccde0b88` ("feat: provider admin test flow"), which swapped the role on the gate's
first branch and rewrote the tests that pinned it. Reproduced on a live Aspire instance with seeded data
on 2026-09-12 — same write shape for every row (`PUT /api/v2/Providers/{p}`, `SupportEmail` changed);
`provideradmin` is bound to `prov1` through `dbo.User.ProviderId`, `test` holds the platform `Admin` role:

| identity | system role | bound to | target | result |
|---|---|---|---|---|
| `provideradmin` | ProviderAdmin | prov1 | **prov2 (foreign)** | **204 No Content — write committed** |
| `provideradmin` | ProviderAdmin | prov1 | prov1 (own) | 204 No Content |
| `test` | **Admin** | — | prov2 | **403 `provider.write_forbidden`** |
| `test2` | User | — | prov2 | 403 `provider.write_forbidden` |

The two roles were effectively swapped: the tenant-scoped `ProviderAdmin` held platform-wide write and the
platform `Admin` was refused the write its role is supposed to carry. The cross-tenant write really
landed — `dbo.Provider` for `prov2` carried the probe value afterwards, not merely a 204 on the wire. An
ordinary interactive password-grant login was enough; no service key was involved.

### A correction to the record

ADR-0094's Context states that "the `ProviderAdmin` role already holds every provider-write permission
key". The RBAC seed shows otherwise: `ProviderAdmin` holds neither `perm:provider:remove` nor
`perm:provider:create`. ADR-0094's body is left as written; the correction lives here.

## Considered options

- **Role `Admin`, or the `provider_name` claim matching the route (chosen).** One key on the write path,
  the same key reads use, and the tenant boundary enforced by data rather than by role name. Cost: the
  interactive `ProviderAdmin` gains write reach it did not have, which is the intent.
- **Keep D9 and add `Admin` back.** Rejected: it fixes only half the defect. The interactive
  `ProviderAdmin` would keep a read tier it cannot write in, and ADR-0018's same-key rule would stay
  broken on the write side.
- **Key both reads and writes off `am`** (ADR-0094's own rejected alternative, re-examined). Rejected
  again, and now superseded: `am` describes how a caller logged in, not what it may do. Authorizing on it
  makes authority a property of the login path, and D10's read asymmetry is the only place that remains
  defensible — there it bounds a *lookup* reach, not a mutation.
- **Grant `perm:provider:remove` to `ProviderAdmin`** to make v2 Deactivate reachable. Rejected: see the
  RBAC consequence below. At the time the alternative was weighed, that key was also the *only* gate on v1
  `DELETE /api/Provider/{Id}`, whose handler ran `service.DeleteProviderAsync(id, ct)` with no caller, no
  authorization and no provider scoping, so granting it would have opened an unscoped cross-tenant delete —
  the exact bug class this ADR closes. The remap is still the right shape (it moves one mapping instead of
  widening a role), and the v1 handlers are no longer the single point of failure: see the v1 consequence
  below.

## Consequences

- **The gate is one expression with one question on the non-admin branch.** A reader of
  `ProviderWriteAuthorization.Authorize` no longer needs to know the caller's identity kind, and neither
  does any of its call sites.
- **The RBAC mapping for v2 Deactivate moves**, from `perm:provider:remove` to `perm:provider:update`
  (held by both roles), leaving Core's shared gate as the fine tenant check. **No role definition
  changes.** This is verbatim the pattern
  [`0059-v2-gateway-rbac-mapping-gap-fixes.md`](0059-v2-gateway-rbac-mapping-gap-fixes.md) D2 (§31-33)
  applied to `POST .../reactivate`.
- **It also supersedes that D2's conclusion about `DELETE`, for v2 only.** D2 closes, unqualified,
  "Deactivate (`DELETE`) stays on `perm:provider:remove` (Admin-only) by design"; for
  `DELETE /api/v2/Providers/{providerName}` that no longer holds. Calling the remap purely "an
  application, not a reversal" would leave an accepted ADR standing false. v1
  `DELETE /api/Provider/{Id}` keeps `perm:provider:remove` and stays `Admin`-only, exactly as D2 decided.
- **A release-runbook item, mandatory.** Seed reconciliation at startup is additive only
  ([`0060-additive-rbac-seed-reconciliation-at-startup.md`](0060-additive-rbac-seed-reconciliation-at-startup.md),
  §26-28: "Changed mappings, changed role→permission assignments ... do **not** propagate"), and the
  importer matches mappings by **endpoint name**. A `v2 Deactivate Provider` entry that keeps its name is
  treated as existing and skipped, so the seed edit alone changes nothing in a non-empty RBAC store. The
  remap requires a one-off RoleManagement UI import with `overwriteExisting=true`. No task performs it.
- **`Admin` regains every provider write**, including the shared gate on
  `POST /api/v2/Providers/{providerName}/Resources`, which is the identity the HEAppE create-resource
  walkthrough was originally verified with.
- **The two v1 provider writes now enforce the same rule, keyed on `provider_id`.** v1 predates
  `provider_name` and scopes by the `provider_id` claim `TryGetProviderId` reads, so the rule is the same
  question asked of the v1 key: a caller carrying a `provider_id` may write only that provider.
  `PUT /Provider` (`src/Core/API/API/ProviderEndpoint.cs:97`) checked only that the claim was *present* and
  then updated the **body**-named provider, so a service key for one provider could update another; it now
  requires `provider.Id` to equal the claim. `DELETE /api/Provider/{Id}` (`:125`) refuses a `provider_id`
  that does not match the route. Each route keeps its previous reach for a caller with **no** `provider_id`
  claim — refused on `PUT` as it already was, allowed on `DELETE`, where `perm:provider:remove` remains the
  gate and the platform `Admin` keeps its reach. No v1 route is removed or renamed and no response shape
  changes. This is what makes a later broadening of `perm:provider:remove` merely a permission decision
  rather than a tenant-isolation failure.
- **A stale `provider_name` claim is still bounded by the mint.** Grant and revocation latency is
  ADR-0094 D5's 30 s cache, unchanged by this decision.
