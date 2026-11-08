# Bearer Token Validation

How the Gateway validates the audience of a user Bearer token issued by the identity provider (IdP).

## Overview

The user Bearer scheme validates the issuer, the lifetime, the signature and the audience of every token. With
strict audience validation on (the default), a token is accepted only when every audience it carries is one the
Gateway is configured to accept. The decision is recorded in
[ADR 0121](../../../architecture/adr/0121-gateway-rejects-user-tokens-with-unconfigured-audiences.md).

The API-key, cookie, internal-token and dev-override schemes and the Core API's `GatewayJwt` scheme are not affected.

## Configuration

| Key | Environment variable | Meaning |
|---|---|---|
| `Idp:Authority` | `Idp__Authority` | Issuer of the user tokens (required). |
| `Idp:Audience` | `Idp__Audience` | The audience of the Gateway (required). |
| `Idp:AdditionalAudiences` | `Idp__AdditionalAudiences` | Further accepted audiences, comma-separated; entries are trimmed and empty entries and duplicates dropped. |
| `FeatureManagement:StrictJwtAudience` | `FeatureManagement__StrictJwtAudience` | Strict audience validation, default `true`. |

Under the Aspire AppHost the flag comes from the boolean parameter `FeatureStrictJwtAudience`, prefilled with `true`.

### `FeatureManagement:StrictJwtAudience`

- Absent or blank: on.
- `true` or `false` (case-insensitive, surrounding spaces ignored): used as given.
- Any other value: the Gateway stops at startup with an error that names the key, never the value.
- The flag is read once at startup; changing it needs a restart.

## The rule

With the flag on, a token is valid only when:

1. its `aud` claim is not empty, and
2. every `aud` value equals `Idp:Audience` or one value of `Idp:AdditionalAudiences`, compared exactly
   (ordinal, case-sensitive).

With no additional audiences this means the token carries exactly the one configured audience.

With the flag off, the IdentityModel default applies: the token is valid when any one of its audiences matches
`Idp:Audience` or `Idp:AdditionalAudiences`.

## Rejection

A token that fails the audience check is answered with `422` and the error code `auth.token_invalid_audience`.

## Upgrading

Before upgrading, list every audience your IdP puts into user tokens in `Idp:AdditionalAudiences`. Keycloak often
adds audiences such as `account` or `broker`, or other client ids. Otherwise set
`FeatureManagement:StrictJwtAudience` to `false`. Without either, every user call answers `422`.

The Gateway's `ClientId` is not added to the accepted audiences automatically.
