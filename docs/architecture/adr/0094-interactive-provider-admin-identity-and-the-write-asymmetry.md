# ADR 0094 - Interactive ProviderAdmin identity: reads open to both identity kinds, writes stay service-key-only (D9)

- Date: 2026-08-13
- Status: accepted

## Decision

> **Amended by [ADR-0105](0105-provider-write-authority-is-the-admin-role-or-the-callers-own-provider.md)
> (2026-09-12).** Two parts of this record are superseded: the **"Writes stay service-key-only"** section
> below (D9 — §34-38 as originally filed, §40-44 once this pointer was inserted) and the rejected alternative **"Key both reads and writes off `am`"**. Everything
> else stands — D5 (the interactive mint), D10 (by-natural-key read reach still differs by
> `IsServiceKeyIdentity`), D11, the self-asserted-claim ingress strip and the fail-closed rule. The
> argument below is left as written: it is the record of what was decided on 2026-08-13.

[ADR-0011](0011-provider-write-authorization.md)'s claims-only identity model is extended from a single
actor — the provider service-key — to **two** actors that both carry a `providerName` claim:

- **Provider service key** (unchanged from ADR-0011): the Gateway resolves it from the vault-key's
  `provider_id`.
- **Interactive ProviderAdmin**: an OIDC human whose effective role is `ProviderAdmin` **and** who has
  a live `AssignedProviderId`. The Gateway mints the same `providerName` claim for them, resolved from
  the DB assignment, inside the `authMethod is EAuthorizationMethod.JwtBearerToken` branch of
  `InternalTokenTransformExtensions` (`ResolveAdministeredProviderNameAsync`). A demoted user (stale
  `AssignedProviderId`, changed role) mints nothing — the predicate is role AND assignment, and both
  halves are evaluated on every mint, each read through a 30 s cache (see Revocation and Grant latency
  below).

Both actors resolve to `ReadScope.Provider` (`Core/Shared/Auth/ReadScope.cs`), but their read reach is
not identical:

- **List reads (D11)** behave the same for both and only ever widen: each general listing unions the
  caller's own provider with whatever its project scope already reached — and, on `GET /providers`,
  with the public providers every authenticated caller sees. Landing in the provider tier therefore
  never costs a caller rows it could see before.
- **By-natural-key reads (D10)** are where the two differ. `CallerReadScope.ResolveProjectScopeFor`
  (`ReadScope.cs:45-56`) leaves the lookup unrestricted for a **service key** on any active record;
  for an **interactive ProviderAdmin** it does so only inside its own provider. Reading another
  provider's record, the interactive caller falls back to its project scope — holding the claim never
  opens another provider's private records.

**Writes stay service-key-only.**
`ProviderWriteAuthorization.Authorize`'s non-admin branch requires `caller.IsServiceKeyIdentity` in
addition to the `providerName` match — an interactive ProviderAdmin whose claim matches the route's
provider is still refused every write behind that gate (provider, location, resource, and both
assignment surfaces — resource-to-location and resource-to-project), 403, regardless of role or claim.

### The asymmetry is intentional, not an oversight

[ADR-0018](0018-read-scope-tiers.md)'s Context bullet at §28 states, in full: *"The provider **write**
path (ADR-0011) already established claims-only, name-keyed scoping: the actor is a provider service-key
identity carrying a `providerName` claim, not an interactive `ProviderAdmin` user. Reads must use the
**same** key, or reads and writes would disagree about who 'is' a provider. An earlier draft that keyed
the read tier on the `ProviderAdmin` role name conflicted with this and with the CONTEXT.md glossary; it
was corrected."* Three sentences — who the actor is, the rule, and the draft it rejected — and this ADR
departs from exactly one clause of the first.

**The clause that is superseded** is the parenthetical description of who the actor is — "not an
interactive `ProviderAdmin` user". D5 makes that no longer exhaustive: the actor set becomes two
identity kinds, as the Decision above records. That is a change to the *cast*, not to the key.

**The clause that is preserved** is the rule itself: reads and writes must use the same key. They still
do. That same bullet's closing sentence shows what it was rejecting — "an earlier draft that keyed the
read tier on the `ProviderAdmin` role name conflicted with this" — and ADR-0018's rejected-alternatives
list (§37) repeats it. It rejected keying the **read tier** on the `ProviderAdmin` **role name** while
writes stayed keyed on the `providerName` claim, which would have let two different mechanisms decide
"who is a provider" for the same caller.
D9 does not revive that proposal and does not touch the read tier's key: `ReadScope.Provider` is still
resolved from the `providerName` claim, for both actors, exactly as ADR-0018 requires. What D9 adds is
an **orthogonal, write-only** check on top of an already-resolved provider tier: *given* a caller the
tier already recognises as "this provider", may they also mutate it? Reads answer yes for both actors;
writes answer yes only for the service key. The "same key" ADR-0018 requires is preserved — both
reads and writes still ask "does `providerName` match the route", and answer identically for that
question. D9 asks a second question only writes need answered, and answers it via the auth method
(`am`), not the role. Recording this here, deliberately: an inbound OIDC ProviderAdmin having a
provider-wide *read* view and no write reach is a designed split of authority, not two components that
independently drifted out of sync.

### The self-asserted-identity trust boundary — the most important rule in this ADR

D9's write gate is only as trustworthy as the `am` claim it reads, and the Gateway-minted token's
`provider_id` claim is only as trustworthy as the same ingress guarantees. Both describe something the
Gateway itself decides — how a caller authenticated, and which provider a service key acts for — and
both were, before this task, capable of being asserted by the caller instead.

**Read this before quoting anything below — the exact scope of the guarantee.** The ingress strip
guarantees that no *self-asserted* identity claim survives; it does **not** guarantee that no
downstream service will mint its own. Those are different statements and conflating them has already
produced one wrong conclusion in an earlier draft of this record. Concretely: the kill-switch
authority check, `KillSwitchAdminHandler.CanManage`, does **not** read the Gateway-minted
`provider_id` for a `ProviderAdministrator` principal **whose assignment Core can resolve** — for that
caller the Core API re-derives its own value, server-side, independently of this ingress fix. The
qualifier is load-bearing, not a hedge: Core injects its re-derived value **only** when the resolver
returns one (`AuthenticationExtensions.cs:125`), so a `ProviderAdministrator` whose assignment does not
resolve keeps the token's own `provider_id` and `CanManage` **does** read it — which is precisely where
the ingress strip carries the whole weight. See "The `provider_id` claim has two independent sources"
below, which states both bounds and what the strip closed in the unresolved case; this section's
guarantee reaches the Gateway-minted claim, never Core's own re-derivation.

Before this task, the Gateway **appended** its own resolved identity (carrying `am = JwtBearerToken`
for a bearer caller) onto the `ClaimsPrincipal` via `AddIdentity`, and `InternalTokenTransformExtensions`
read `am` back with `FindFirstValue`, which returns the **first** matching claim across
`principal.Identities` in registration order. Because the validated IdP identity is the principal's
**first** identity — and the only one built from the caller's own token, since the introspection identity
(`ServiceExtensions.cs:395-398`) and the Gateway's claims identity (`:437`) are both appended after it —
an inbound token that itself carried an `am` claim, e.g. a forged `am=ApiKey` on an otherwise-ordinary
interactive login, would be read back **instead of** the Gateway's own value. `Caller.IsServiceKeyIdentity` (and therefore D9's write gate) would then
classify a human as a service key, and `ProviderWriteAuthorization` would authorize the write.
Separately, and for the same underlying reason, `InternalTokenTransformExtensions`'s
`provider_id → providerName` resolution (see the `provider_id` section below) relayed whatever
`provider_id` claim already sat on the *incoming* principal, unconditionally and with no auth-method
check — so a forged `provider_id` on an interactive login rode along too, and could redirect
`CanManage`'s ownership check to a provider the caller was never assigned to.

The fix does not reorder or overwrite in place; it **strips every inbound self-asserted identity claim
from every identity** before the Gateway appends its own (`TryDiscardSelfAssertedIdentityClaims`,
`ServiceExtensions.cs:427`, immediately before the `AddIdentity` at `:437` that is the last statement
in `OnTokenValidated` — nothing after the strip adds an identity), driven by one list — `am` and
`provider_id` together, not `am` alone:

```csharp
private static readonly string[] SelfAssertedIdentityClaimTypes =
    [EAuthorizationMethod.ClaimType, AuthConstants.ProviderIdClaimType];
```

The rule this establishes, and the one every future ingress change must honour:

> **A claim describing how a caller authenticated, or which provider it acts for, is Gateway-owned. An
> inbound copy of either is never trusted — it is discarded, not merely outranked.**

Discarding rather than reordering matters: a reordered-but-still-present forged claim would still be
readable by any consumer that enumerates claims (`principal.Claims`, `FindAll`) instead of taking the
first match. After the strip, exactly one claim of each type exists on the principal and it is always
the Gateway's (or, for `provider_id` on the Bearer path, none at all — see below).

**Fail closed if the strip cannot complete.** A claim that cannot be removed does not silently
survive: `TryDiscardSelfAssertedIdentityClaims` reports the failure, and the caller logs an error
(claim type, user id, path), tags the activity, and calls `context.Fail(...)`, which the JWT bearer
handler turns into a 401. Warn-and-continue was considered and rejected. The branch is reachable only
for a token that already carries `am` or `provider_id` — and no legitimate IdP token in this system
carries either, since both are minted by the Gateway itself, never by the IdP. An honest caller's
token therefore never enters this branch and cannot be rejected by it; continuing on an unremovable
claim, by contrast, would let a forged claim through to every downstream consumer while the Gateway
still believed it had stripped it — the exact failure this mechanism exists to prevent. A future
reader tempted to soften this to a warning should re-derive that reachability argument first: it holds
identically for `am` and `provider_id`, not just the claim that happened to motivate the fix.

### The `provider_id` claim has two independent sources — the Gateway's mint and Core's own re-derivation

`KillSwitchAdminHandler.CanManage` (`:276`) keys kill-switch authority off `CallerInfo.ProviderId`:
`caller.IsPlatformAdmin || (caller.ProviderId is Guid p && p == providerId)`. The value it reads does
**not** come solely from the Gateway's mint. The kill-switch routes are hosted in the Core API process
(`src/Core/API/Program.cs:176` → `UserorgAdapterExtensions.cs:51` → `HeappeAdapterExtensions.cs:68`),
which validates the Gateway-issued internal token in its own `GatewayJwt` `OnTokenValidated`
(`src/Core/API/Authentication/AuthenticationExtensions.cs:122-136`). There, for a bearer principal
whose role is `ProviderAdministrator` (the constant is `RoleInfoConstants.ProviderAdministratorRoleName`
— the same string `"ProviderAdmin"` used everywhere else in this record), `AAAsync` (`:385-414`)
independently re-derives a `provider_id` via `IProviderIdResolver.GetProviderIdAsync(sub)`, and
`OnTokenValidated` rebuilds the principal with that value in place of whatever the Gateway's mint put,
or didn't put, in the token's `provider_id`. Two conditions bound it, and both matter to anyone
auditing this path: the re-derivation runs only on the bearer path (`AAAsync:392/406` reads the
Gateway's `am` claim — so it is independent of the Gateway's `provider_id`, but *not* of its `am`),
and it injects only when the resolver returns a value; a null resolution leaves the token's own
claim standing. **This is deliberate, pre-existing, documented behaviour**
([ADR-0080](0080-kill-switch-reads-caller-at-the-endpoint-and-scopes-provider-in-the-handler.md): "a
platform admin has no `provider_id` claim (Core injects it only for ProviderAdministrator)"): a human
ProviderAdmin **does** hold kill-switch authority over the provider they administer.

**Corrected premise.** An earlier draft of this ADR reasoned about "human PAs must not gain kill-switch
authority" as a risk this task's D5 mint could create, and treated closing it at Gateway ingress as the
fix. That premise was wrong on both ends: the authority was never at risk from D5 (the D5 mint produces
a `providerName` claim, never a `provider_id`, for an interactive caller — see the Decision above), and
the authority itself was already granted, independently, by Core's `ProviderAdministrator` re-derivation,
before this task touched anything. **This task neither granted nor removed the *legitimate* kill-switch
authority of an assigned human ProviderAdmin** — that property predates it and is untouched by it.
Recorded here as a corrected premise so it is not re-derived incorrectly again.

**What the strip did remove here: an invariant that rested on IdP configuration, now enforced.** The
correction above must not be over-read into "the ingress fix was irrelevant to kill-switch authority".
For the caller Core's resolver cannot resolve — the null-resolution bound stated just above — the
token's own `provider_id` decided kill-switch authority, and nothing in this codebase decided what that
claim contained. Given an inbound token that carries a `provider_id` claim, the chain ran end to end:

1. Before the fix, the transform read `provider_id` off the **inbound** principal
   (`InternalTokenTransformExtensions.cs:160`, first match across identities — the IdP token's own claim)
   and relayed it verbatim into the minted token (`:190`), with no `am` gate.
2. Core replaces that claim only when its resolver returns a value (`AuthenticationExtensions.cs:125`);
   with no resolvable assignment the forged value survives onto the principal.
3. `GetCallerInfo` parses whatever claim is standing (`UserOrg.Shared/AuthenticationExtensions.cs:52-55`)
   into `CallerInfo.ProviderId`, and `CanManage` (`:276-277`) compares exactly that against the target
   provider — a forged value equal to the target's id passes.
4. The routes are reachable by the role: every kill-switch route is mapped to
   `perm:provider:admin`/`perm:platform:admin` in `roleManagmentRBAC.json`.

**Step 1 is a precondition, not something a caller can supply — read the chain accordingly.** The
inbound principal is built from an IdP-signed JWT validated with `ValidateIssuer` /
`ValidateIssuerSigningKey` (`Gateway/Startup/ServiceExtensions.cs:253-261`); the only other identity
appended before the transform contributes `preferred_username` and `role` and nothing else (`:376-393`);
the Role-Management cookie principal is Gateway-built and carries no `provider_id`
(`RoleManagementAuthMiddleware.cs:116-131`). No claim mapper in this repository emits `provider_id` —
the same reachability argument the fail-closed branch above rests on. **So what stood here was a
dependency on IdP configuration, not a live exploit, and this record must not be cited as evidence of
one.** Links 2-4 are what make closing it worthwhile: they are unconditional code paths, so the moment
the assumption about the IdP stopped holding — a mapper added, a federated issuer, a second realm — the
authority followed with no further mistake required. The strip moves that guarantee out of the IdP's
configuration and into the Gateway's enforcement, where a reader of this repository can verify it.

"`ProviderAdmin` role without a resolvable assignment" is an ordinary state, not a contrived one: the two
halves come from different stores — the role claim from the Gateway's permissions snapshot
(`UserPermissionsCache`, Forge.Core), the assignment from Core's `User.AssignedProvider`
(`UserRepository.GetProviderIdByIdpUserIdAsync`) — and their caches even expire at different rates (30 s
against 60 s), so they can disagree without anyone misconfiguring anything. Recorded plainly because a
record that undersells its own fix invites a future reader to undo it: for that caller, the strip is what
removes the dependency on IdP configuration, and it is the only thing in this codebase that does.

**The division of responsibility this leaves in place.** The Gateway's ingress strip (below) guarantees
that no *self-asserted* `provider_id` survives onto the Gateway-minted token. It does **not** guarantee
that no downstream service mints its own — and Core's `ProviderAdministrator` re-derivation is exactly
that: a second, independent mint, performed by a different service, for a different reason, never
routed through the Gateway's `provider_id` claim at all. Two services derive `provider_id`
independently, on purpose; a future reader auditing kill-switch authority must check both, not just the
Gateway ingress path.

**The Gateway-minted token's own `provider_id` claim, on its own terms — this part genuinely closed a
gap.** Before this task, nothing gated `provider_id` on auth method: `InternalTokenTransformExtensions`'s
`provider_id → providerName` resolution ran unconditionally, and the transform separately relayed
whatever `AuthConstants.ProviderIdClaimType` claim was already present on the *incoming* principal,
again with no `am` check. So an interactive caller's Gateway-minted token could carry a forged
`provider_id`, and "an interactive caller's Gateway-minted token never holds a self-asserted
`provider_id`" rested on the IdP not issuing that claim, not on anything the codebase enforced.

That gap is now closed by **two** guards, deliberately not one — the ingress strip is the general rule,
the second is local to the one call that consumes the claim:

1. **At ingress**, `provider_id` is a member of `SelfAssertedIdentityClaimTypes` (above) and is stripped
   from every inbound identity on the Bearer path, before `InternalTokenTransformExtensions` ever reads
   it. This is the same mechanism as `am`, not a separate one.
2. **At the transform**, the `provider_id → providerName` resolution
   (`InternalTokenTransformExtensions.cs:183-191`) is itself gated on
   `authMethod is EAuthorizationMethod.ApiKey`. This gate was added after the strip, and is not
   redundant with it: it is what keeps the D5 interactive mint (resolved a few lines above, from the DB
   assignment) from being overwritten by a `provider_id` on a bearer principal, whatever its origin. A
   reader auditing only one of the two guards will draw the wrong conclusion about the other's necessity.

What is left ungated is the raw relay, not the resolution: the transform passes `providerClaim`
verbatim into `GenerateInternalTokenAsync` (`:193`), so the minted token carries whatever `provider_id`
the principal still had. On the Bearer path, after the strip, that is always the empty string. Only the
service-key path (`ApiKeyAuthenticationHandler`, its own scheme, never traversing the Bearer pipeline
the strip runs in) can produce a non-empty `provider_id` on the Gateway-minted token itself; that is
what these guards protect, and it is a real, closed gap. For a human ProviderAdmin it governs
`CanManage` only where Core's re-derivation does not: with a resolvable assignment Core's value wins
regardless, without one this claim is the value `CanManage` reads.

Separately, unaffected by the strip: `TokenService.cs:81` **always** writes the `provider_id` key onto
the internal token, so for an interactive caller the Gateway-minted claim is an **empty string, not
absent** — now guaranteed rather than merely typical, since ingress guarantees no inbound value
survives to be relayed. Every read site parses before trusting it (`Guid.TryParse`, or an
`IsNullOrEmpty` guard), and every one fails closed on the empty string —
`UserOrg.Shared/AuthenticationExtensions.cs:52`, its v1 twin
`src/Core/API/Authentication/AuthenticationExtensions.cs:192/218`,
`InternalTokenTransformExtensions.cs:160` (read) and `:184` (its `IsNullOrEmpty` + `TryParse` guard),
`UserOrg.Ddi/.../GetSyncDetailsEndpoint.cs:63-68`. Nowhere in `src/` is the **raw claim** read for
presence alone (`FindFirst(...) is not null`, `HasClaim`), so the empty string never behaves as
"present". The two presence-only reads that do exist — `ProjectResourceEndpoint.cs:29`
(`openIdModel.ProviderId is not null`) and `AuthenticationExtensions.cs:359`
(`openIdModel.ProviderId.HasValue`) — test the `Guid?` that `TryGetOpenIdUserInfoModel` already parsed
at `:218`, into which the empty string parses as `null`; they are correct as written, and are named here
so the next auditor does not read them as counter-examples. Documented here because the shape
(present-but-empty, not absent) reads as an omission to anyone who has not checked. (This empty-string
claim is what `OnTokenValidated` replaces for a `ProviderAdministrator` principal whose `AAAsync`
re-derivation returns a value, before `CallerInfo` is built — see above.)

Kept here as history, not as an open item: the Gateway-level relay existed, it was the same bug class
as the `am` one above, and both were closed by one mechanism
(`TryDiscardSelfAssertedIdentityClaims`) — not two independent fixes that happened to land in the same
task; the `ApiKey` gate on the resolution followed as the second, local guard described above.
Nothing about the Gateway-minted token's self-asserted `provider_id` rests on the IdP any more.
Its reach over kill-switch authority splits exactly along the resolver bound: for a human ProviderAdmin
Core **can** resolve, the authority is Core's own re-derivation, by design, neither opened nor closed by
this fix; for one it **cannot**, the strip is what turned an IdP-configuration assumption into an
enforced one — see the corrected premise above for the full chain, including why that was an unenforced
invariant rather than a live exploit.

### Revocation

Interactive-ProviderAdmin claim resolution is cached (`IProviderIdentityResolver`, `HybridCache`,
`Expiration` and `LocalCacheExpiration` both **30 seconds**). After
`DELETE /api/v2/Users/{idpUserId}/ProviderAdmin`, a caller loses the `providerName` claim within that
30 s cache window — plus the remaining life of any internal token already minted before the
revocation, since the cache only bounds re-resolution, not an already-issued token.

### Grant latency

**A newly promoted ProviderAdmin is answered 403 for up to 30 s.** Both entries are held 30 s and
neither is evicted by the role write (`UserPermissionsCache.cs:38-39`,
`ProviderIdentityResolver.cs:15-16`), but a grant waits on one of them where the revocation above is
bounded by whichever expires first: only the Gateway's `userperms:{idpUserId}` permissions snapshot,
not the resolver's claim cache.

The `provider-admin/*` routes are gated on `perm:provider:admin`/`perm:platform:admin`, matched any-of
(`roleManagmentRBAC.json:1462-1472`, `:1881-1890`; `PermissionMatcher.cs:47-67`) — and a promotee, a
platform `User` (`:2256-2266`), carries neither before the write; `perm:provider:admin` is carried by
the `ProviderAdmin` role alone (`:2515`). Until the snapshot expires it still yields those
pre-promotion keys, and they are matched **before** that same transform re-reads the snapshot and mints
(`AuthorizationHelpers.cs:143-151`, called from `InternalTokenTransformExtensions.cs:145-155`, ahead of
`:173-176`) — so the route answers **403 "Forbidden: insufficient permissions"**.

Permission and `providerName` then arrive together, on the same first post-expiry request. The
provider-name lookup is itself gated on the role claim carried by that same snapshot
(`InternalTokenTransformExtensions.cs:218-232`, the only caller of `GetAssignedProviderNameAsync`), so
for a promotion `assignedprovidername` is resolved **cold, not lapsed** — no entry is written for a
caller whose snapshot does not already say `ProviderAdministrator`.

The provider-name cache bounds a grant only in the other case: a user **already**
`ProviderAdministrator` whose provider assignment changes. There the role claim never flips, and
`assignedprovidername:{idpUserId}` — empty-string not-found sentinel included — is the whole lag.

### `provider-admin/*` routes are own-provider by contract, not a D11 site

D11 forbids the general listings (`GET /providers`, `/locations`, `/resources`) from narrowing what a
provider-tier caller can already see, so each of them is widened to **own provider ∪ what the caller's
project scope already reached** — plus, on `GET /providers` only, the **public** providers every
authenticated caller sees (there is no public-row union on locations or resources; their pre-existing
non-admin tier was project-reachability alone). The dedicated `provider-admin/*` routes
(`GET api/v2/ProviderAdmin/Locations`, `GET api/v2/ProviderAdmin/Projects`,
`GET api/v2/ProviderAdmin/Providers`) are a different
contract: they pin the provider tier to **own provider only**, with no union. This is deliberate, not
an oversight D11 failed to reach:

1. **No reach is lost.** Because the general listings already union own-provider with the caller's
   project scope, a caller reclassified into the Provider tier by the D5 mint still sees every
   project-reachable row through the general route. D11's "must not lose rows visible today" property
   holds platform-wide; only the dedicated route answers with a narrower, own-provider-only contract.
2. **The route's purpose is that narrower view.** `provider-admin/*` exists beside the general listing
   (the dedicated-route precedent set by [ADR-0058](0058-provider-admin-locations-scoping-and-rbac.md))
   specifically to be the administrator's console for the provider they administer, not a second
   general listing with a different shape.

### The dropped EXISTS guard on `GetProjectsProviderAdmin`

The plan called for resolving the route's provider name to a surrogate id, then adding a second EXISTS
guard against an active `Provider` on the join, mirroring `LocationDetailsQuery`. The second guard
turned out to be a tautology: the name→id resolution already reads through
`ProviderConfiguration`'s `ProviderActive` global query filter (`!p.IsDeleted`), so a deactivated or
unknown provider never produces an id, and the query short-circuits to an empty page before the join
exists. Nothing downstream can then compare against an id that is not already known-active.
`LocationDetailsQuery` keeps its own EXISTS guard for a different reason, not as a shortcut it later
took: a `Location` carries a **mandatory** provider FK and that query also pages when the resolved
provider id is `null` (admin/scoped tiers), so a location owned by a deactivated provider must still
be excluded even when there was no name filter to resolve. `Project` has no provider dimension at all
when the caller supplies no provider filter, so there is nothing for a second guard to protect once
the first resolution has already gated on `ProviderActive`.

## Context

- The plan for `GET api/v2/ProviderAdmin/Projects` required both a provider service-key **and** an
  interactive ProviderAdmin to be able to list their provider's projects — the first caller kind
  ADR-0011/0018 covers, the second did not exist as a first-class identity before this task.
- Minting a provider-tier claim for an interactive human, with no other change, would have hit two
  hazards resolved by D9 and the `am` fix above: (1) the `ProviderAdmin` role already holds every
  provider-write permission key, so `providerName` alone would have opened the entire provider write
  surface to any human PA; (2) the pre-existing claim-precedence bug meant even the *write gate itself*
  could be forged from a bearer token before this task's fix landed.

## Consequences

- **`ReadScope.Provider` now covers two identity kinds with different write authority.** A reader of
  `Caller.ProviderName is { }` alone can no longer assume the caller is a service key — that inference
  held before this task and no longer does (`AuthConstants.ProviderNameClaimType`'s doc, `ReadScope`'s
  doc, and `GetProjectsEndpoint`'s stale comment were corrected in this same change for exactly this
  reason).
- **Two client-visible contract changes**, both required by D11, and they do not cover the same
  endpoints. Neither is purely additive for an existing service key: the first *removes* rows it used
  to get back, the second *adds* rows it did not:
  - **Filter semantics, on `GET /providers` and `GET /locations` only.** On those two the caller's
    `?name=`/`?providerName=` used to be overwritten with the provider tier's own name
    (`name = providerScope.ProviderName`; `filterProviderName = providerScope.ProviderName`); it is now
    **honoured**, and the tier is carried separately (`ownProviderName` on the filter). A provider
    service key that filtered by another provider's name used to receive **its own** provider's rows
    (the filter was discarded); it now receives only what its tier already permits of the *named*
    provider — for a typical service key (no project scope, private target) that is **nothing**, though
    on `GET /providers` a **public** provider's row does come back. The filter narrows the tier; it
    never widens it. `GET Providers/{providerName}/Resources` is **not** part of this change: its
    provider is a **route segment**, not a query filter, and it was already applied regardless of tier
    (`FilterProviderName: provider`) before this task. Its only change is the project-scope union below
    — inert for a service key, which never receives project records at all
    (`InternalTokenTransformExtensions.cs:169-177` fills them on the bearer path only).
  - **Widened tiers, on all three.** The **providers** list for a service key widens from own-only to
    **own + public + any private provider its project scope reaches**; `/locations` and `/resources`
    likewise add the caller's project scope to the own-provider set. Required by D11's "must not
    narrow" rule for the interactive actor, and unavoidable without giving the two identity kinds
    different list shapes. Confidentiality impact is nil: the newly-visible rows are the public
    providers already enumerable by any authenticated caller on the same route, plus rows the caller's
    own projects already reach.
  - So "service keys are unchanged by this task" holds for the **locations and resources** lists (a
    service key has no project scope to union in) but not for the **providers** list, and the **filter
    semantics** changed for a service key on `/providers` and `/locations`.
- **`GetProjectsEndpoint` (`GET /projects`) itself is unchanged** — it still has no provider tier and
  still scopes every non-admin caller (service key or interactive) to `caller.Scope`. The
  provider-wide project view this ADR title refers to is a **separate, dedicated** route
  (`GET api/v2/ProviderAdmin/Projects`), the same shape ADR-0058 established for locations — the
  `GetProjectsEndpoint` comment that used to claim "no provider identity ever gets a provider-wide
  project view" was accurate about that endpoint but was read as a platform-wide claim; it is
  corrected to say what is actually true post-D1/D5.
- **ADR-0018's "separate `/v2/administration/*` endpoints" rejection is narrowed, not overturned.**
  ADR-0018 rejected duplicating every read endpoint behind an admin-only variant for a pure data-scope
  difference; that still holds — `provider-admin/*` is not an admin variant of the general listing, it
  is the ADR-0058-precedented, deliberately narrower **provider's own console**, gated by
  `perm:provider:admin`, and it exists beside the general listing rather than instead of it.
- **The Gateway-minted token's `provider_id` relay is closed, not merely narrowed.** Both self-asserted
  identity claims — `am` and `provider_id` — are stripped from every inbound identity at ingress, on the
  Bearer path, before `InternalTokenTransformExtensions` or any downstream consumer reads them
  (`TryDiscardSelfAssertedIdentityClaims`, see the trust-boundary section above). No self-asserted
  identity claim survives ingress. The two were the same bug class and were closed by one mechanism,
  not fixed independently; the `provider_id → providerName` resolution is additionally gated on
  `am = ApiKey` at the call site — see "The `provider_id` claim has two independent sources" above for
  both guards. **This leaves a human ProviderAdmin's *legitimate* kill-switch authority untouched** —
  Core re-derives that independently for the `ProviderAdministrator` role, by design (ADR-0080) — but it
  **did** convert an assumption about IdP claim mapping into an enforced guarantee for a role-holder
  whose assignment Core cannot resolve, because Core then leaves the token's own `provider_id` standing.
  See the same section for both bounds, for why the pre-fix state was an unenforced invariant rather
  than a live exploit, and for why they must not be conflated.

## Rejected alternatives

- **Key both reads and writes off `am` (rejected).** Would collapse D9's read/write split and require
  every by-id/list read site to add a service-key-only branch, re-opening exactly the private-data
  exposure ADR-0018 closed for interactive callers reading providers/locations/resources.
- **Key the read tier off the `ProviderAdmin` role name instead of the claim (rejected, restated from
  ADR-0018).** Still wrong for the same reason ADR-0018 rejected it: the role is not the scoping
  mechanism, and a role-keyed tier would disagree with a claim-keyed write gate about who "is" a
  provider for a caller whose role and claim happen to diverge (e.g. a demoted user whose token has
  not yet expired).
- **Widen `provider-admin/*` list sites additively, matching D11 (considered, declined; see above).**
  Would have made the dedicated routes behaviourally redundant with the general listings while still
  answering a narrower question by name — no reach gained, only a second general listing with a
  different shape.
- **Reorder the Gateway's `AddIdentity` call instead of stripping the inbound self-asserted claims
  (rejected).** Leaves a forged claim present and readable by any consumer that enumerates rather than
  takes the first match; stripping removes the value entirely rather than merely outranking it. Applies
  identically to `am` and `provider_id` — both are stripped by the same mechanism, not reordered.
