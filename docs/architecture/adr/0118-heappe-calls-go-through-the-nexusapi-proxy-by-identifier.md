# ADR 0118 - Every HEAppE call goes through the NexusApi proxy, addressed by the HEAppE identifier

- Date: 2026-10-13
- Status: accepted, partially superseded by [ADR 0120](0120-nexusapi-settings-under-token-exchange-and-a-startup-gate.md) (decisions 2, 3 and 4: `TokenExchange:Nexus:BaseUrl` replaces service discovery and `services__nexusapi__*`, `TokenExchange:Nexus:*` replaces `Nexus__*`, and with the token exchange flag off userorg no longer checks `Enabled`)
- Relates to: #349 (this change does not close it: the token exchange and the Redis tier stay open)

## Context

A location carried the HEAppE address as an `AssignedLocationValue` `HEAPPE_URL`, and every module built its
`HttpClient` base address from it. NexusApi now sits in front of HEAppE: it holds the instance registry, the
Keycloak clients allowed to talk to each instance, and proxies `/api/heappe/{identifier}/...`. Calling HEAppE
directly from userorg bypasses that registry, and a URL in a database row is the wrong key for an instance NexusApi
already names.

## Decision

1. **The HEAppE identifier is the key.** `HEAPPE_IDENTIFIER` is a `HeappeIdentifier` (`UserOrg.Shared`):
   `^[a-z0-9][a-z0-9-]{0,63}$` after trimming and lower-casing. `HEAPPE_URL` stays in the data and in logs only; no
   HEAppE HTTP is sent to it. A link without a valid identifier cannot be routed: it is skipped with one warning per
   link (TemplatesInfo), fails permanently (`HEAPPE_IDENTIFIER_INVALID`, resource creation) or is left out of the
   query (repositories).
2. **One door.** `IHeappeConnectionFactory` (`Heappe.Contracts`) answers `ForPublicAsync`, `ForServiceAsync` and
   `ForUserAsync` for an identifier. The base address is `https+http://nexusapi/api/heappe/{identifier}/`, resolved
   by service discovery (`services__nexusapi__*`). The factory asks `IHeappeInstanceDirectory`, which reads the
   NexusApi registry (instances and clients, cached 60 s and refetched after an invalidation), so a disabled or unknown
   instance is `HeappeInstanceNotFound` and an unreachable NexusApi is `NexusUnavailable`.
3. **userorg signs in to NexusApi as a service account** (`Nexus__ClientId`, `Nexus__ClientSecret`, validated at
   start). `NexusServiceTokenProvider` caches the token until shortly before `expires_in`;
   `NexusServiceTokenHandler` attaches it and retries once on 401.
4. **NexusApi is part of the AppHost.** Under `aspire run`: a container resource (own Postgres 17 `config`
   database, a one-shot `migrate`, a one-shot `import` of a generated file with the `userorg` service account, the
   `heappe-mock` client and the `heappe-mock` instance routed to the WireMock, then the server with
   `RUN_MIGRATIONS=false`, port 8080, no HTTP health check because the Production image maps `/health` only in Development). Whether the container or
   an external NexusApi at `NexusApiBaseUrl` backs `nexusapi` is a dashboard dialog answered
   after `Build()`, like the database source (`Parameters:NexusApiSource=Container|External` skips it); the
   container chain is explicit-start. Publish mode always takes the external
   service, with the base URL as a required parameter. The API references it without `WaitFor`, so a host whose
   NexusApi image cannot be pulled still starts.
5. **Resilience follows the instance.** The data client's pipeline is selected by identifier, so one failing
   instance does not open the circuit of another.

## Consequences

- A deployment must provide `services__nexusapi__*` (or `services__nexusapi__default__0`) to the Core API and the
  `Nexus__ClientId` / `Nexus__ClientSecret` pair; the API refuses to start without the pair.
- The HEAppE stand-in (`heappe-mappings/`) answers the NexusApi registry and token paths as well as the proxied
  HEAppE paths, and the E2E seed carries `HEAPPE_IDENTIFIER=heappe-mock`.
- The exchange of the caller's token for a HEAppE token (TokenDelegation) is not part of this decision; ADR 0119
  adds it behind `TokenExchangeEnabled`.
