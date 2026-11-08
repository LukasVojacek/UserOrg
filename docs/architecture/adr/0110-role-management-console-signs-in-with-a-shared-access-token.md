# ADR 0110 - The role-management console signs in with a shared access token

- Date: 2026-09-25
- Status: accepted
- Supersedes: [ADR-0108](0108-role-management-operators-sign-in-through-the-idp.md)
- Relates: ADR-0109 (audit coverage)

## Context

ADR-0108 replaced the console's shared access token with an OpenID Connect sign-in against the gateway's
IdP, admitting only operators whose UserOrg role is `Admin`. The console is meant to stay reachable with an
access token alone, without any IdP client. The deployments configure only `RoleManagement:AccessToken`,
and the gateway built with ADR-0108 refused to start there: `RoleManagementOptions.Validate()` required
`RoleManagement:Oidc:ClientId` and `ClientSecret`, so every start ended in `OptionsValidationException:
Invalid RoleManagement configuration`.

## Decision

1. **Sign-in is the shared access token again.** `RoleManagementAuthMiddleware` exchanges `?t=<token>` on a
   console path for the `RoleManagementAuth` cookie and redirects to the console without the query. A
   missing or wrong token answers `404`. `RoleManagement:AccessToken` is required and the gateway fails to
   start without it. The console has no IdP scheme, no IdP client and no `RoleManagement:Oidc` settings.
2. **The token comparison is constant-time.** Both values are hashed with SHA-256 and compared with
   `CryptographicOperations.FixedTimeEquals`, replacing the ordinal comparison ADR-0108 criticised.
3. **The cookie is `SameSite=Strict` again**, `HttpOnly` and `Secure`, sliding within
   `CookieExpirationMinutes`. `Lax` existed only for the IdP callback.
4. **The session is not revalidated.** The Admin-role check, the revalidation interval and the absolute
   lifetime of ADR-0108 are gone: with no per-person identity there is no role to check.
5. **Sign-in and sign-out stay audited (ADR-0109).** `gateway.role_management.signed_in` Success is written
   before the cookie is issued, fail-closed. A wrong token writes `gateway.role_management.signed_in`
   Denied with `gateway.role_management_sign_in_failed`, fail-closed. A missing token writes nothing. The
   actor is `rm-` plus the first 16 hex digits of the token's SHA-256, never the token.
6. **Sign-out stays, without the IdP.** `POST /role-management/signout` checks the antiforgery token,
   removes the cookie and writes `gateway.role_management.signed_out` Success, fail-open. ADR-0108 called
   the endpoint antiforgery-protected but it was not: the handler binds no form, so the antiforgery
   middleware's result was never enforced. The handler now validates the request itself.
7. **The AppHost generates the token** as the secret parameter `RoleManagementAccessToken` and puts it in the
   dashboard's "Forge management" link, as before ADR-0108. The dev realm's `role-management-console` client
   is removed.

## Consequences

- Deployments configure `RoleManagement__AccessToken` and nothing else for the console.
- Everyone who knows the token is the same operator in the audit log. ADR-0108's per-operator
  accountability is given up.
- The token travels in a URL once per sign-in, so it can reach browser history and proxy logs.
- Rotating the token does not end existing sessions: the cookie does not reference the token and slides.
  A session ends when it stays idle for `CookieExpirationMinutes` or its operator signs out.
- The gateway no longer depends on `Microsoft.AspNetCore.Authentication.OpenIdConnect`.

## Alternatives rejected

- **Keep ADR-0108 and provision an IdP client per environment**: the console must not depend on an IdP.
- **Offer both, token and IdP**: two ways in, two configurations to keep valid, and the token path still
  carries no per-person identity.
