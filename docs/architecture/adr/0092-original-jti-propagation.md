# ADR 0092 - Propagate the original token id (`original_jti`) through the internal token

- Date: 2026-08-05
- Status: accepted

## Decision

1. The internal token **keeps its own unique `jti`**. It is a distinct token and stays independently
   identifiable (replay/issuance analysis on the internal token remains possible).
2. The Gateway reads the incoming token's `jti` and writes it, **verbatim**, into a new
   `original_jti` claim on the internal token it mints.
3. **No prefixing in UserOrg.** The `onrtro:`-style prefix belongs to the component that mints the
   identity; UserOrg relays. Prefixing here would break the value equality that makes the chain
   joinable across components.
4. When there is **no incoming token** (ApiKey, Cookie, public, S2S), the internal token's own
   `jti` is used as the chain value. Consumers resolve the value as `original_jti ?? jti`.
5. The original `jti` becomes part of the **internal-token cache key**. `InternalTokenProvider.cs:52-70`
   serves minted tokens from a `HybridCache` keyed
   `internaltoken:{authMethod}:{subject}:{systemRole}:{providerId}:{projectHash}:{originalJti}`; baking a claim into
   the token without keying on it would serve a cached token carrying a *stale* original jti after an
   IdP token refresh inside the TTL — recording the wrong token id, i.e. the exact failure this
   feature exists to prevent.

## Context

The platform requires every component to log the token ID (`jti`) of the token that arrived with a
request, so the full chain can later prove that the user actually asked for the action. Example
value shape: `"jti": "onrtro:aaa95cb6-93ce-66da-c72f-f90d51837449"` — the prefix identifies the
component that minted the identity.

In UserOrg the chain breaks at the Gateway. The Gateway validates the caller's IdP token, then
replaces `Authorization` with a freshly minted internal JWT
(`InternalTokenTransformExtensions.cs:189`) whose `jti` is an unrelated `Guid.NewGuid()`
(`TokenService.cs:78`, `:187`). The original `jti` is never read, never forwarded, never logged —
repo-wide it appears only at those two mint sites. Downstream (Core API) therefore has no way to
name the user token that authorized the action.

Facts established during the grill (state/research/incoming-jti.md):
- The incoming `jti` is available on `HttpContext.User` only for the Bearer/JwtBearer scheme, under
  the short claim type `"jti"` (`MapInboundClaims=false`, ServiceExtensions.cs:41,240).
- ApiKey, Cookie and PublicWithInternalToken build their principals from scratch — there is no
  incoming JWT and no `jti`. Service-to-service and public requests reach the internal-token
  transform with no original token at all.

## Consequences

- The chain "IdP token → internal token → downstream action" becomes joinable on one value across
  components without any of them changing their token format.
- `original_jti` is **not** a request correlation id: one IdP access token serves many requests, so
  the same value legitimately appears on many actions. Request correlation stays with
  `traceparent` / `X-Trace-Id`; the two must not be conflated in dashboards or queries.
- Every consumer must implement the `original_jti ?? jti` fallback, otherwise non-Bearer traffic
  silently has no chain value.
- The internal token grows by one claim (~50 bytes).
- Cache entries become effectively per-IdP-token instead of per-user. A user with one active token
  keeps today's hit rate; a user with several concurrent tokens (multiple devices) multiplies entries.
  Entries are short-lived, so the growth is bounded by the token TTL.
- `IInternalTokenProvider.GenerateInternalTokenAsync` gains a parameter — ripple to 2 call sites plus
  `Tests/.../InternalTokenConcurrencyTests.cs:61`.

## Rejected alternatives

- **Overwrite the internal `jti` with the original.** Simplest and matches the platform wording
  most literally, but the internal token loses its own identity — two internal tokens minted from
  the same user token become indistinguishable, and per-internal-token replay detection dies.
- **Prefix the value with `uorg:` on relay.** Would satisfy a reading of the platform example where
  each component stamps itself, but destroys value equality across components — the one property the
  chain requirement actually needs.
- **Forward the original `jti` in a separate HTTP header instead of a claim.** The value would be
  unsigned and spoofable by any caller that can reach the backend directly; a claim inside the
  signed internal token is not.
