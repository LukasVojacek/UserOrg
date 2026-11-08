# ADR 0121 - The gateway rejects user tokens that carry an unconfigured audience

- Date: 2026-11-04
- Status: accepted
- Relates to: #399, credential-service `FeatureManagement:StrictJwtAudience`

## Context

The gateway's user Bearer scheme validated the token audience with the IdentityModel default: a token passed when
any one of its `aud` values was `Idp:Audience` or one of `Idp:AdditionalAudiences`. A token issued for several
services, only one of them the gateway, was therefore accepted, so a token meant mainly for another client could be
replayed against the gateway.

## Decision

1. **Strict by default.** With `FeatureManagement:StrictJwtAudience` on, a user Bearer token is valid only when its
   `aud` is non-empty and every value equals `Idp:Audience` or a value of `Idp:AdditionalAudiences` (exact,
   ordinal). With no additional audiences this means exactly the one configured audience.
2. **Flag.** Default `true`; absent or blank is on; `true` or `false` is used as given; any other value stops the
   host at startup, naming the key and never the value. The flag is read once at startup, so changing it needs a
   restart. Off restores the IdentityModel default (any one audience matches).
3. **Scope.** Only the user Bearer scheme. The API-key, cookie, internal-token and dev-override schemes and the
   Core API's GatewayJwt scheme are unchanged.
4. **Response.** A rejected token answers the existing 422 `auth.token_invalid_audience`.
5. **Kill switch.** The flag is a permanent operational switch, no expiry.

## Consequences

Every deployment must list every audience its IdP puts into user tokens (Keycloak often adds `account`, `broker` or
other client ids) in `Idp:AdditionalAudiences` before upgrading, or set the flag to `false`; otherwise every user call
answers 422. The gateway's `ClientId` is not added to the accepted set automatically.
