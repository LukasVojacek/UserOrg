# ADR 0119 - The caller's token is exchanged per HEAppE instance behind TokenExchangeEnabled

- Date: 2026-10-26
- Status: accepted, partially superseded by [ADR 0120](0120-nexusapi-settings-under-token-exchange-and-a-startup-gate.md) (decisions 1 and 6, the flag gate: the flag is read once at startup and gates the registrations)
- Relates to: [ADR 0001](0001-gateway-internal-token.md) context, [ADR 0074](0074-internal-precondition-failure-fast-fails-as-500.md), [ADR 0093](0093-log-token-id-in-every-component.md), [ADR 0109](0109-audit-covers-every-p1-and-p2-operation.md), [ADR 0112](0112-logging-modes.md), [ADR 0118](0118-heappe-calls-go-through-the-nexusapi-proxy-by-identifier.md)

## Context

ADR 0001 rejected a token exchange between the gateway and the backend. This decision does not reopen it: it is
scoped to the outbound hop from the Core API to one HEAppE instance behind the NexusApi proxy (ADR 0118). Until now
`HeappeConnectionFactory.ForUserAsync` forwarded the caller's broad token to every HEAppE instance. NexusApi lists,
per instance, the Keycloak client HEAppE accepts and its scopes, so the API can ask Keycloak for a token whose
audience is that client only.

## Decision

1. **Flag.** `FeatureManagement:TokenExchangeEnabled` (default `false`), evaluated per call through
   `IFeatureManager`. Off is today's behaviour with no IdP call. The flag is independent of `HEAppEAuthHeader`:
   `HEAppEAuthHeader` chooses how the token reaches HEAppE, `TokenExchangeEnabled` which token it is.
2. **Exchange.** After `ResolveAsync`, `IHeappeTokenExchanger` calls TokenDelegation
   `ExchangeDelegatedTokenAsync("heappe-token-exchange", token, { Audience = route.ClientId, Scope = scopes joined by a space })`
   against `{TokenExchange:Authority}/protocol/openid-connect/token` (trailing slash trimmed), authenticated as the
   client `TokenExchange:ClientId` / `TokenExchange:ClientSecret`. The exchanged token
   replaces the caller's token for both the bearer and the session-code path. There is no fallback to the caller's
   token.
3. **Cause codes.** `token-exchange-unavailable`: Nexus lists no client, no scopes, a scope that is not an
   RFC 6749 scope-token or a client id with whitespace. `token-exchange-failed`: the IdP refused or timed out, the
   breaker is open, or the client is not configured. Both are per-instance causes that follow ADR 0074 with no new
   HTTP mapping: TemplatesInfo reports them in `failures[].cause` (200 with `unavailable-instances` when partial,
   503 when every instance failed); direct user endpoints such as DownloadChangedFiles keep their existing 500.
   Caller cancellation is rethrown.
4. **Cache.** `IMemoryCache`, key SHA-256 of the caller's token plus client id plus sorted scopes, lifetime
   min(`expires_in` minus 30 s, 300 s); no entry when `expires_in` is missing or at most 30 s; failures are not
   cached; no single-flight.
5. **Resilience.** The delegation HttpClient `tokendelegation:heappe-token-exchange` drops the inherited handler
   and gets its own: attempt 5 s, total 10 s, no retry, breaker MinimumThroughput 5, SamplingDuration 10 s,
   BreakDuration 30 s.
6. **Configuration.** The delegation client is registered only when the client id, the secret and the authority
   are all present; otherwise one startup Warning names the missing settings, never their values, and a flag-on
   call fails with `token-exchange-failed`.
7. **Logging and telemetry.** Every failure logs one Warning per identifier and cause per minute with the
   identifier, cause, exception type and, for `TokenExchangeException`, `Kind` and `StatusCode`; never the exception
   object, its message, the IdP body, a token or a secret. The `TokenDelegation` log category is held at Error
   because the library's Warning carries the IdP error description. ServiceDefaults exports
   `TokenDelegationTelemetry.MeterName` and `ActivitySourceName`.
8. **Audit.** The exchange is not audited per call, like internal token issuance (ADR 0109); the trail joins it by
   trace id and `auth.jti` (ADR 0093). The exchanged token carries no `act` claim, so HEAppE sees the user.

## Kill switch

The flag is a permanent operational kill switch, not a release flag: with it on, an IdP outage fails every HEAppE
user call closed. Recovery is `FeatureManagement__TokenExchangeEnabled=false` and a restart of `api`. The Heappe
module owner decides, triggered by the TokenDelegation exchange failure meter or a sustained rise of
`token-exchange-failed` Warnings.

## Shared client

The exchange reuses the introspection client, which is confidential and in the `aud` of user tokens. Keycloak
grants it exchange permission per HEAppE client, never realm-wide. Rotating its secret restarts both gateway and
api. A dedicated exchange client replaces it once a non-HEAppE audience appears (risk R-04).

## Quality scenarios

| id | attribute | stimulus | response measure |
|---|---|---|---|
| QS-01 | Security | Flag on, Nexus lists a client and scopes | HEAppE receives only the exchanged token; 0 calls carry the caller's token |
| QS-02 | Security | A failed exchange whose exception message and IdP 400 body carry the caller token, the exchanged token and the client secret, through the real delegation pipeline | 0 occurrences in log records, `Error.Description` or cache keys |
| QS-03 | Performance | Repeated calls with the same token, client and scopes, token valid more than 30 s, one replica | 1 IdP call within the cache lifetime |
| QS-04 | Performance | IdP slow or unreachable | At most 5 s per attempt, 10 s total, 1 attempt; under 100 ms with the breaker open |
| QS-05 | Availability | IdP down, flag on | `Result.Failure(token-exchange-failed)`, no unhandled exception, 100 % of cases |
| QS-06 | Observability | IdP refuses or is unreachable | At most one Warning per identifier and cause per minute with identifier, cause code and trace id; 0 Error records; 0 token or secret values; meter registered |
| QS-07 | Security | User disabled after an exchange | No HEAppE call with that token later than 300 s (fake clock) |

## Consequences

- `api` now needs `TokenExchange__ClientId`, `TokenExchange__ClientSecret` and `TokenExchange__Authority`; the AppHost
  forwards them in the external branch (parameters `TokenExchangeClientId`, `TokenExchangeClientSecret` and the IdP
  authority) and the local Keycloak branch, and `FeatureTokenExchangeEnabled` (default false).
- With the flag off the caller's broad token still reaches HEAppE (risk R-03).
