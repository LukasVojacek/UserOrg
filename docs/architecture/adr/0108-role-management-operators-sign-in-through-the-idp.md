# ADR 0108 - Role-management operators sign in through the IdP

- Date: 2026-09-23
- Status: superseded by [ADR-0110](0110-role-management-console-signs-in-with-a-shared-access-token.md)

## Context

The role-management console (`/role-management`, the gateway's Blazor RBAC UI) was opened by one shared
secret. `RoleManagementAuthMiddleware` read it from the `?t=` query, compared it to
`RoleManagement:AccessToken` with an ordinal, non-constant-time comparison, and minted a cookie whose
subject was `rm-{sha256(token)[..16]}`, with a made-up e-mail and a hard-coded `Admin` role. Everyone who
knew the token was the same operator, so no record could say which administrator changed the RBAC
configuration. The secret also travelled in URLs (the Aspire dashboard link carried it), and the cookie slid
with no cap, so a session once opened never had to re-prove anything.

`AUTHENTICATION.md` described an OIDC Bearer flow with an admin-role check that the code never implemented.
A Bearer header cannot ride a browser navigation, and an access token in `?t=` would repeat the exposure.

## Decision

1. **Sign-in is an OpenID Connect authorization-code flow with PKCE against the gateway's IdP**
   (`Idp:Authority`), on its own scheme `RoleManagementOidc`, whose sign-in scheme is the existing
   role-management cookie. A console request without a valid cookie is challenged to it. The handler keeps
   inbound claim names (`MapInboundClaims = false`) and requires HTTPS metadata outside Development, as the
   JwtBearer setup does.
2. **Only platform administrators get in.** After the ID token is validated, the operator's effective role
   is read from `IUserPermissionsCache` for the IdP `sub`, the snapshot the proxy path uses, and must be
   `Admin`. The cookie then carries the IdP `sub`, `preferred_username`, role `Admin`, a fresh
   `rm_access_id` (so the `RoleManagementAccess` policy is unchanged) and `rm_session_started`.
3. **The shared secret is removed, with no break-glass path.** `RoleManagementOptions.AccessToken`, the
   AppHost parameter, its environment variable and the `?t=` query are gone. If the IdP is down, the console
   is down.
4. **The session is revalidated and capped.** One `RoleManagementSessionValidator` decides whether a session
   may continue: expired past `AbsoluteSessionLifetime` (8 h) from `rm_session_started`, revoked when the
   role is no longer `Admin`, lookup-failed when the lookup throws, valid otherwise. The cookie applies it at
   most once per `SessionRevalidationInterval` (5 min) and rejects a ticket past its lifetime at once; a
   `RevalidatingServerAuthenticationStateProvider` applies it to open Blazor circuits on the same interval,
   and `AuthorizeRouteView` with `[Authorize]` on the page takes the page away from an invalidated circuit.
   Revocation window: **5 min 30 s** for a role change in UserOrg (interval plus the 30 s permissions-cache
   TTL), **8 h** for a change made only at the IdP, because the gateway does not call the IdP after
   sign-in.
5. **Outages fail closed.** A failed role lookup ends the session; a sign-in cannot complete without the
   IdP.
6. **There is an explicit sign-out.** `POST /role-management/signout`, antiforgery-protected and under the
   console policy, removes the cookie and signs out of the OIDC scheme, which redirects to the IdP's
   `end_session_endpoint`. No ID token is stored to skip the IdP's logout confirmation, to keep the cookie
   small. It is a POST so that a cross-site link cannot end a session.
7. **The cookie becomes `SameSite=Lax`.** The IdP callback is a cross-site navigation; a `Strict` cookie set
   in that redirect chain is not sent on the next request and the sign-in loops. `Lax` still withholds it
   from cross-site subrequests and POSTs.
8. **Audit.** `gateway.role_management.signed_in` Success, and Denied with
   `gateway.role_management_not_admin` or `gateway.role_management_sign_in_failed`, are written fail-closed:
   no cookie exists without its record. Every `gateway.role_management.signed_out` (the user's sign-out,
   Success; each forced end, Denied with the verdict's reason) is written fail-open through
   `GatewayAuditExtensions.AuditBestEffortAsync`, so an audit fault never keeps a session alive or turns a
   sign-out into a `500`. Records name the operator by IdP subject.
9. **Dedicated confidential clients are the accountability boundary.** The console uses its own client
   (`RoleManagement:Oidc:ClientId`/`ClientSecret`) per environment, provisioned in that environment's realm
   before the release that ships this change. Who may use the console is then decided by the IdP account and
   the UserOrg `Admin` role, per person.
10. **The local client is dev-only.** The dev realm
    (`src/UserOrg.AppHost/externalServices/keycloakRealm/master-realm.json`) carries a confidential client
    `role-management-console` with a committed dev secret, standard flow only, PKCE `S256` required,
    redirect URI `https://localhost:*` and post-logout redirect URIs `+`. The port wildcard is what survives
    the random ports of `aspire start --isolated`; it matches any path, so the callback path is enforced by
    the gateway's OIDC handler, which completes a sign-in only on `/role-management/signin-oidc`. The realm's
    `testClient` cannot serve instead: its only redirect URI is the relative `/*` with an empty `rootUrl`,
    which Keycloak resolves against its own origin. The local client carries no accountability weight. The
    AppHost passes its id and secret unconditionally, so the options validation passes whatever the IdP
    choice; against an external IdP the console has no local client and sign-in fails there.

## Consequences

- Every sign-in, sign-out and later RBAC record can name the operator.
- Operators need an IdP account with the UserOrg `Admin` role; the console has no other way in.
- A demoted operator loses the console within 5 min 30 s, open circuit included; an IdP-only disable takes
  up to 8 h.
- Each deployed environment needs its console client provisioned before the release; this is an operations
  task outside the repository.
- The gateway takes a dependency on `Microsoft.AspNetCore.Authentication.OpenIdConnect`, pinned with
  `Microsoft.AspNetCore.Authentication.JwtBearer` because both depend on
  `Microsoft.IdentityModel.Protocols.OpenIdConnect`.
- The OIDC handler is a remote handler and initializes on every request, so a plain-HTTP `Idp:Authority`
  outside Development fails every gateway request, not only console sign-ins.

## Alternatives rejected

- **Bearer or API key on the console** (the old document): a browser cannot attach a header to a
  navigation, and a token in the query string repeats the exposure.
- **Keep the shared secret as break-glass**: it keeps an unattributable way in and a secret to rotate.
- **Check `Admin` only at sign-in**: the sliding cookie would keep a demoted operator in indefinitely.
- **Reuse `testClient` locally**: its relative redirect URI resolves to Keycloak's own origin.
