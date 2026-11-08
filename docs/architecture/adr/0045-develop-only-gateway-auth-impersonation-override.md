# ADR 0045 - Develop-only Gateway auth override: impersonate an existing user by `sub`, driven by Aspire parameters

- Date: 2026-06-30
- Status: accepted (amended 2026-07-12)

## Update 2026-07-12 — collapsed to a single `Enabled` flag, bearer-as-sub only

The three Aspire parameters (`DevAuthOverrideEnabled` / `DevAuthOverrideSub` / `DevAuthOverrideUseBearerAsSub`) are replaced by a **single** `DevAuthOverrideEnabled` flag. The two impersonation modes (fixed configured `Sub` vs. bearer-carried idpid) are collapsed into one: **when enabled, the Gateway always treats the bearer string itself as the caller's idpid (`sub`)**. The per-request bearer-as-sub behaviour that was previously behind `UseBearerAsSub` (and used by the E2E suite) is now the only mode.

What this changes versus the original decision below:

- `DevAuthOverrideOptions` now has only `Enabled`; `Sub` and `UseBearerAsSub` are removed.
- The selector gate is `isDevelopment && Enabled` (no `Sub`-non-empty / `UseBearerAsSub` sub-condition).
- `Enabled` now **defaults to `false` (opt-in)**. With the fixed-`Sub` mode gone, an empty `Sub` no longer exists to keep a default-on flag harmless, so the safe default is off; a real IdP token flows through the normal `Bearer` path until a developer explicitly enables the override.
- `preferred_username` is set to the impersonated `sub` (not the fixed `"dev-impersonation"` marker, which is removed) so the legacy `/api/UserInfo` user-init filter resolves the same user everything else keys by idpid.
- Usage: `Authorization: Bearer <idpid>` where `<idpid>` is a valid `IdpId` (3–150 chars) of a user present in the DB — **not** a real signed JWT.

The safety gates (Development-only registration; run-mode-only AppHost wiring) are unchanged. The original 2026-06-30 decision is retained below as the historical record.

## Decision

Add a **Develop-only** authentication path to the Gateway that removes the Dev-environment dependency on the real IdP. When enabled, the Gateway treats **any non-empty `Bearer` string** as authenticated and constructs a `ClaimsPrincipal` whose `sub` is supplied by the developer through an **Aspire AppHost parameter (custom input)**. Everything downstream of authentication is reused unchanged — the principal flows through the existing permission gate and the existing YARP internal-token transform, which mint the same internal token forwarded to UserOrg today.

The override is **impersonation by `sub` only**:

- The single configurable value is the impersonated user's **`sub` (= idpid)**. The intended use is a restored DB backup that already contains a user with a known idpid.
- The fake principal carries `auth_method = JwtBearerToken`, which is exactly what makes the existing transform (`InternalTokenTransformExtensions.cs:155-161`) resolve **role and project roles from the database** via `UserPermissionsCache.GetAsync(sub)` (`AuthorizationHelpers.cs:96`). Roles are therefore **real DB roles**, not fabricated.
- The principal also carries a synthetic `preferred_username` set to the constant **`"dev-impersonation"`** (a value that self-identifies the Dev override), purely to satisfy the transform's `ArgumentNullException.ThrowIfNull(usernameClaim)` guard (`InternalTokenTransformExtensions.cs:152`). It is cosmetic — it is not used for authorization and the role/projectRoles claims are overwritten from the DB snapshot. It is deliberately a fixed marker, not the `sub`, so anything that surfaces the username makes the Dev origin obvious.

The override is gated by a **triple safety mechanism** — all three must hold for it to do anything:

1. **`IsDevelopment()` at registration.** The custom scheme and its handler are only added to the auth pipeline when `app.Environment.IsDevelopment()`. In any non-Development environment the scheme does not exist; the selector cannot forward to it.
2. **Explicit config flag + a configured `Sub`.** In Development the policy-scheme selector forwards a `Bearer` request to the override scheme only when `DevAuthOverride:Enabled = true` (env var `DevAuthOverride__Enabled`) **and** `DevAuthOverride:Sub` is non-empty. `Enabled` **defaults to `true`** (developer decision 2026-06-30) so the override is on out-of-the-box in Dev; the **`Sub`-non-empty condition is what keeps default-on harmless** — with no `Sub` configured the selector falls through to the real `Bearer` path, so a real IdP token is never hijacked. Default `Sub` is empty.
3. **Run-mode-only wiring at the AppHost.** The Aspire parameters and the `WithEnvironment` calls that surface `DevAuthOverride__Enabled` / `DevAuthOverride__Sub` are created only inside `if (builder.ExecutionContext.IsRunMode)`, so during `aspire publish` they do not exist and a deploy manifest never carries them. (This is preferred over `ExcludeFromManifest()` on the parameters alone: the Gateway resource *references* the `Sub` parameter via `WithEnvironment`, so excluding only the parameter would leave a dangling `{param.value}` reference in the manifest — run-mode gating drops the reference too.) If the env var leaked into a real environment anyway, gate (1) still kills it.

## Context

- In Dev the Gateway authenticates every interactive request against the real IdP (`AddJwtBearer` + `TokenIntrospectionService`, fail-closed on introspection failure). This couples local development and DB-backup-based testing to IdP availability and to the IdP holding the same users as the backup.
- The Gateway already has a **`DynamicAuthScheme` policy selector** (`ServiceExtensions.cs:88-162`) that routes each request to one of four schemes (`Bearer`, `ApiKey`, role-management cookie, public). A Develop override is naturally a **fifth scheme** the selector picks under a flag — no change to the existing four.
- Authentication and internal-token minting are **already decoupled**: minting happens in a YARP transform that reads claims off the authenticated principal, and resolves role/project-roles from `UserPermissionsCache` keyed on `sub`. A principal with the right `sub` and `auth_method` is therefore sufficient to drive the real downstream behaviour.
- The standard `JwtBearer` handler structurally **cannot** accept an arbitrary non-JWT string, so the override must be a custom `AuthenticationHandler`, not a tweak to the existing Bearer options.

## Considered options

- **Custom Develop-only scheme selected under a flag, impersonate by `sub`, roles from DB (chosen).** Smallest blast radius: one new handler + one selector branch + one Aspire param. Reuses the permission gate and token transform verbatim, so impersonated requests exercise the *real* authorization path. Roles stay truthful to the DB. Triple-gated against escaping Develop.
- **Disable JwtBearer validation / set `Idp:Introspection:Enabled=false` and relax `TokenValidationParameters`.** Rejected: still requires a structurally valid signed JWT (any non-empty string would not parse), so it does not meet "any string is a valid token", and it weakens the *real* validation code in place rather than adding an isolated, clearly-named bypass.
- **Fabricate role / projectRoles claims from parameters too.** Rejected for now (explicit developer decision): the backup already carries the user's real roles, and keying everything on `sub` means the DB stays the single source of truth for permissions. Fewer params, no second code path, no risk of a fabricated role diverging from what the user really has. (Revisit only if testing a not-yet-provisioned identity is ever needed.)
- **A standalone "dev IdP" container (e.g. a stub OIDC server).** Rejected as heavier: another moving part to run and seed, when the only thing Dev actually needs is a principal with a known `sub`.

## Consequences

- **Cross-component change, but additive.** Touches the **Gateway** (new `DevOverrideAuthenticationHandler`, register-when-Development, one selector branch, a `DevAuthOverrideOptions`) and the **AppHost** (`DevAuthOverride__Enabled` / `DevAuthOverride__Sub` parameters with custom input, wired via `WithGatewayEnvironment`, `ExcludeFromManifest`). No change to the four existing schemes, the transform, the permission gate, or UserOrg/Core.
- **Real authorization is still exercised.** Because impersonation reuses the permission gate and DB-backed role resolution, a developer impersonating a low-privilege backup user gets that user's real `403`s. The override fakes *authentication*, not *authorization*.
- **`preferred_username` is synthetic under the override.** Anything in Dev that surfaces the username from the internal token will show the `sub` value. Acceptable for a Dev-only path; called out so it is not mistaken for a bug.
- **Introspection is bypassed entirely** on the override path (the custom handler never calls `TokenIntrospectionService`), which is the whole point — no IdP round-trip.
- **Safety rests on gate (1).** The flag and manifest-exclusion are convenience/defence-in-depth; the load-bearing guarantee is that the scheme is *not registered* outside Development. A boundary test asserts the handler type is absent from the auth schemes when the environment is not Development.
- **`auth_method = JwtBearerToken` is required**, not incidental: it is the switch that makes the transform pull role/projectRoles from the DB snapshot. An override principal without it would forward an internal token with an empty role and break authorization silently.
