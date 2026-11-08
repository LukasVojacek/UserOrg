# Role Management Authentication

This document describes how an operator opens the Role Management Blazor UI at `/role-management`, how long
the session lives, and how it ends. The decision behind it is
[ADR-0110](../../../../architecture/adr/0110-role-management-console-signs-in-with-a-shared-access-token.md),
which supersedes the IdP sign-in of ADR-0108.

## Overview

The console is opened with one shared access token, `RoleManagement:AccessToken`. It is not connected to any
identity provider:

1. The operator opens `/role-management?t=<token>`.
2. The gateway compares the token with the configured one and issues the `RoleManagementAuth` cookie.
3. Later requests carry the cookie and need no token.
4. The session ends when the cookie expires or the operator signs out.

## Sign-in

`RoleManagementAuthMiddleware` handles every path under `/role-management`, also under a path base such as
`/userorg/role-management`:

| request | response | audit |
|---|---|---|
| valid cookie | continues to the console | none |
| cookie present but invalid or expired | `401` "Authentication expired. Please provide a valid access token." | none |
| no cookie, no `?t=` | `404` | none |
| no cookie, wrong `?t=` | `404` | `signed_in` Denied |
| no cookie, right `?t=` | `302` to `/role-management` without the query, cookie issued | `signed_in` Success |

The token is compared in constant time: both values are hashed with SHA-256 and compared with
`CryptographicOperations.FixedTimeEquals`. A wrong token answers `404` so that the console's existence is not
confirmed.

The cookie claims are:

| claim | value |
|---|---|
| `sub` | `rm-` plus the first 16 hex digits of the token's SHA-256 |
| `preferred_username`, `name` | `admin-` plus the first 8 hex digits of the same hash |
| `email` | `admin-<8 hex digits>@role-management.local` |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | `Admin` |
| `rm_access_id` | a fresh id for this sign-in, required by the `RoleManagementAccess` policy |

Everyone who knows the token signs in as the same `sub`. `rm_access_id` tells two sign-ins apart.

## Session model

The cookie is `HttpOnly`, `Secure`, `SameSite=Strict` and slides within `CookieExpirationMinutes`. The session
is not revalidated and has no absolute lifetime. Rotating the token does not end existing sessions, because
the cookie does not reference the token.

## Sign-out

The page's "Sign out" button posts to `POST /role-management/signout`, under the `RoleManagementAccess`
policy. The handler validates the antiforgery token and answers `400` without it, then removes the cookie and
answers `200`. A POST, not a GET, so that a cross-site link cannot end an operator's session.

## Audit records

| action | outcome | reason | when | posture |
|---|---|---|---|---|
| `gateway.role_management.signed_in` | Success | none | cookie issued | fail-closed: a failed write fails the sign-in, no cookie |
| `gateway.role_management.signed_in` | Denied | `gateway.role_management_sign_in_failed` | wrong token | fail-closed |
| `gateway.role_management.signed_out` | Success | none | the operator signs out | fail-open (`AuditBestEffortAsync`) |

A Success record names the operator by the token-derived `sub` (`ActorType` `user`) and carries
`rm_access_id` in `Data`. A Denied record's actor is `anonymous`. No record carries the token. A fail-open
write that fails logs one Warning and never turns a sign-out into a `500`.

## Configuration

`appsettings.json`, section `RoleManagement`:

```json
{
  "RoleManagement": {
    "AccessToken": null,
    "CookieExpirationMinutes": 60,
    "CookieName": "RoleManagementAuth"
  }
}
```

- **`AccessToken`**: required. Set it through the environment (`RoleManagement__AccessToken`), never in
  source control, as a random value of at least 32 characters.
- **`CookieExpirationMinutes`**: greater than 0. The code default is 30, `appsettings.json` sets 60.
- **`CookieName`**: not empty.

The gateway fails to start with `OptionsValidationException: Invalid RoleManagement configuration` when any of
these is invalid.

### Local development

The AppHost generates the token as the secret parameter `RoleManagementAccessToken` (32 characters) and passes
it as `RoleManagement__AccessToken`. The "🛠️ Forge management" link in the Aspire dashboard carries it in
`?t=`.

## Architecture

| component | role |
|---|---|
| `Startup/RoleManagementAuthMiddleware` | exchanges the token for the cookie, audits the sign-in |
| `Features/RoleManagement/RoleManagementSignOutApi` | `POST /role-management/signout` |
| `Features/RoleManagement/RoleManagementOptions` | the settings and their validation |
| `Startup/ServiceExtensions.AddAuthenticationServices` | registers the cookie scheme, answers `401`/`403` instead of redirects |

Pipeline order (`Program.cs`): `UseAuthentication()`, `UseRoleManagementAuth()`, `UseAuthorization()`,
`UseAntiforgery()`, then `MapRoleManagementFeature()`.

## Troubleshooting

- **The gateway does not start, "Invalid RoleManagement configuration"**: `RoleManagement__AccessToken` is
  empty inside the process. Check the value the container received, for example with `docker inspect`.
- **`404` on `/role-management`**: no cookie and no valid token. Open the link with `?t=<token>`.
- **`401` on `/role-management`**: the cookie expired or was issued by another gateway key ring. Open the
  token link again.
