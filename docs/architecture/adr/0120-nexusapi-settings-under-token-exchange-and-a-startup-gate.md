# ADR 0120 - NexusApi settings live under TokenExchange:Nexus, and the token exchange flag gates the registrations at startup

- Date: 2026-11-02
- Status: accepted
- Supersedes: [ADR 0118](0118-heappe-calls-go-through-the-nexusapi-proxy-by-identifier.md) decisions 2 (base address by service discovery, and in part the `Enabled` check by userorg), 3 (`Nexus__*` keys) and 4 (`services__nexusapi__*`); [ADR 0119](0119-caller-token-is-exchanged-per-heappe-instance.md) decision 1 (the flag read per call) and decision 6 in part (registration is also gated on the flag)
- Relates to: [ADR 0118](0118-heappe-calls-go-through-the-nexusapi-proxy-by-identifier.md), [ADR 0119](0119-caller-token-is-exchanged-per-heappe-instance.md), T-040

## Context

ADR 0118 reached NexusApi through service discovery (`https+http://nexusapi`, `services__nexusapi__*`) and always
registered the registry client with a required service account (`Nexus__ClientId`, `Nexus__ClientSecret`). ADR 0118
then evaluated `FeatureManagement:TokenExchangeEnabled` on every call. A deployment that runs with the exchange off
still had to provide a NexusApi service account it never needed, the NexusApi address hid behind a service name a
non-Aspire deployment has to fake, and the registered graph (TokenDelegation, the registry) could disagree with the
flag.

## Decision

1. **One section.** The NexusApi settings bind from `TokenExchange:Nexus`: `BaseUrl`, `ClientId`, `ClientSecret`,
   `DirectoryCacheTtl`. The exchange client (`TokenExchange:ClientId`, `ClientSecret`, `Authority`) stays separate
   from the NexusApi service account. Section `Nexus` and `NexusOptions.SectionName` are removed; no alias.
2. **BaseUrl replaces service discovery.** `TokenExchange:Nexus:BaseUrl` is always required, in both modes: an
   absolute http or https URI at the host root only, without path, userinfo, query or fragment. Every HEAppE call goes to
   `{BaseUrl}/api/heappe/{identifier}/`, the trailing slash of `BaseUrl` normalized. https is not enforced.
3. **The flag is read once at startup.** `FeatureManagement:TokenExchangeEnabled` is read from configuration with
   the result of `GetValue<bool>` for every value that parses; absent or blank is `false`. The `EnabledFor` object
   form and any value that is not a plain boolean fail startup naming the key. Flipping the flag needs a restart.
4. **Flag on.** The NexusApi registry, its service-account token provider, `HeappeInstanceDirectory`,
   TokenDelegation with its client, `HeappeTokenExchanger` and `TokenExchangeConfigurationWarning` are registered.
   Calls are routed by the registry as in ADR 0118 and user calls carry the exchanged token as in ADR 0119.
   `TokenExchange:Nexus:ClientId` and `ClientSecret` are required at startup.
5. **Flag off.** Only `ProxyOnlyHeappeInstanceDirectory` and `PassThroughTokenExchanger` are registered: every HEAppE
   call (user, service, public, probes) goes to `{BaseUrl}/api/heappe/{identifier}/` with no registry query and no
   `Enabled` check, and user calls carry the caller's token unchanged. NexusApi answers 404 for an unknown or
   disabled instance. `HEAPPE_URL` is not used for routing.
6. **Startup validation.** One `IValidateOptions<TokenExchangeOptions>` (`NexusOptionsValidator`) checks `BaseUrl`
   and `DirectoryCacheTtl` (one second to one hour) in both modes and the NexusApi service account with the flag on.
   Every failure names the full key and never the value.
7. **Startup log.** One Information line names the mode: `HEAppE routing mode: token exchange` or
   `HEAppE routing mode: pass-through`, with no address, client id or secret.
8. **Kill switch.** The flag stays a permanent operational kill switch, owner the Heappe module owner, no expiry.
   The three-settings rule of ADR 0119 decision 6, its startup Warning and `token-exchange-failed` stay in force.

The missing NexusApi service account fails startup with the flag on, unlike the exchange client's Warning: without
the service account the registry cannot be read and every flag-on HEAppE call fails, so starting would only hide
the fault.

## Quality scenarios

| id | attribute | stimulus | response measure |
|---|---|---|---|
| QS-08 | Deployability | Flag off, only `TokenExchange:Nexus:BaseUrl` set | The API starts; 0 startup failures in the boot test |
| QS-09 | Security, Availability | Flag off, user calls | 0 requests to the registry or the NexusApi token endpoint; the caller's token reaches `{BaseUrl}/api/heappe/{id}/` unchanged on 100 % of user calls |
| QS-10 | Operability, Security | Flag on with `TokenExchange:Nexus:BaseUrl`, `ClientId` or `ClientSecret` missing, or an invalid `BaseUrl` or flag value | Startup fails naming the key and never the value |

ADR 0119 QS-01 to QS-07 and risks R-03 and R-04 stay in force with the flag on: 0 HEAppE calls carry the caller's
token.

## Rejected alternatives

- **Keep the registry lookup with the flag off.** It would keep the `Enabled` check in userorg, but it needs the
  NexusApi service account in a mode that otherwise does not touch it, and the proxy already rejects an unknown or
  disabled instance.
- **Keep the per-call flag check.** The registered graph could diverge from the flag, and TokenDelegation would be
  composed in a deployment that never exchanges.
- **Route by `HEAPPE_URL` with the flag off.** It bypasses NexusApi, which ADR 0118 made the only door.

## Consequences

- Key mapping, old to new: `services__nexusapi__*` -> `TokenExchange__Nexus__BaseUrl`;
  `Nexus__ClientId` -> `TokenExchange__Nexus__ClientId`; `Nexus__ClientSecret` -> `TokenExchange__Nexus__ClientSecret`;
  `Nexus__DirectoryCacheTtl` -> `TokenExchange__Nexus__DirectoryCacheTtl`.
- Upgrade: set `TokenExchange__Nexus__BaseUrl` (always) and `TokenExchange__Nexus__ClientId` /
  `TokenExchange__Nexus__ClientSecret` (flag on) before deploying; the old keys are ignored. Rollback to an older
  image needs the old `Nexus__*` and `services__nexusapi__*` keys back; the flag alone is not a rollback across this
  release.
- An http `BaseUrl` sends the caller's token, and with the flag on the NexusApi client secret, in cleartext. http is
  acceptable only on a network segment that is not shared; production uses https. There is no startup Warning for
  it (R-03).
- With the flag off the caller's broad token is forwarded through the proxy for any valid identifier; NexusApi, not
  userorg, gates unknown and disabled instances (R-03).
- The AppHost wiring of these keys is T-040-02; until it lands an AppHost-started API fails startup on the missing
  `TokenExchange:Nexus:BaseUrl`.
