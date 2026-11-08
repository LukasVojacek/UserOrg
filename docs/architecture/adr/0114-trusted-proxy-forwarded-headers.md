# ADR 0114 - Both hosts apply forwarded headers from trusted proxies only

- Date: 2026-09-29
- Status: accepted
- Relates: ADR-0109 and ADR-0088 §5 (what the audit records, unchanged), ADR-0002 (the in-process YARP proxy)

## Context

API audit rows carried `SourceIp` 127.0.0.1 or the gateway's address, never the client's (#333). Neither the
API nor the Gateway applied forwarded headers, so `RemoteIpAddress` was always the previous hop. The gateway's
YARP chain set `X-Forwarded-For` with `Set`, which dropped whatever the ingress had written, and StepUpLogging
reads the audit `SourceIp` and the request summary's `ClientIp` from `RemoteIpAddress`
(`TrustForwardedHeaders=false`).

The API rate limiter (AspNetCoreRateLimit) was configured with `RealIpHeader: X-Real-IP`,
`ClientIdHeader: X-ClientId` and `ClientWhitelist: [127.0.0.1]`. Both headers are client-settable, so any
client could pick its own rate-limit key or claim the whitelisted client id. Deployments mount their own
`appsettings.json` (ADR-0076), so a fix in the example file alone reaches no deployment.

The deployed topology is client, ingress, gateway, API. There is no CDN in front of the ingress.

## Decision

**Both hosts run ASP.NET Core's `ForwardedHeadersMiddleware` first in the pipeline, with
`XForwardedFor | XForwardedProto` and `ForwardLimit 1`, and trust only the proxies in the `ForwardedHeaders`
section** (`src/UserOrg.ServiceDefaults/Http/TrustedProxyForwardedHeaders.cs`, registered in
`AddServiceDefaults`, applied by `UseTrustedProxyForwardedHeaders` right after `builder.Build()` in both
`Program.cs`).

- `ForwardedHeaders:KnownProxies` holds IP addresses and `ForwardedHeaders:KnownNetworks` CIDR networks.
  Configured entries replace the framework's loopback default. With both lists empty or absent, only loopback
  is trusted.
- Options validation fails the host start on an entry that does not parse, and on a wildcard network
  (`0.0.0.0/0`, `::/0`). An IPv4 network wider than /16 logs a Warning at start.
- Outside Development, a host with no entries logs a Warning at start naming the `ForwardedHeaders` section.
- In run mode the AppHost sets `ForwardedHeaders__KnownProxies__0=127.0.0.1` and `__1=::1` on the API and the
  Gateway, so the dev topology states its trusted hop explicitly.
- The gateway's YARP chain appends to `X-Forwarded-For`; `X-Forwarded-Proto`, `-Host` and `-Prefix` stay on
  `Set`. The gateway's own middleware has already applied the ingress's proto, and a client-sent
  `X-Forwarded-Host` never reaches the API.
- The gateway removes a client-sent `X-Real-IP` and `X-ClientId` before the forwarded-header transforms.
- The API forces `IpRateLimiting:RealIpHeader` and `ClientIdHeader` to the empty string and clears
  `ClientWhitelist` with `PostConfigure<IpRateLimitOptions>`, so the limiter keys on the forwarded client
  address whatever a mounted file says. 127.0.0.1 is dropped from the client whitelist and from
  `IpRateLimitPolicies:IpRules` in the example file.

### Trust assumptions

- Deployments list the ingress in the gateway's `ForwardedHeaders` and the gateway in the API's.
- The ingress overwrites or appends `X-Forwarded-For` with the address it received the connection from.
- The gateway is reachable only through the ingress, and the API only through the gateway.
- The audit `SourceIp` is exactly as trustworthy as that list.

### Keys every deployment sets

- `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`, in both hosts.
- `IpRateLimiting:RealIpHeader`, `IpRateLimiting:ClientIdHeader` and `IpRateLimiting:ClientWhitelist` set
  empty in mounted files. The code forces them empty in a `PostConfigure` either way, so removing the keys
  does not fall back to the library default `X-Real-IP`.

### Deploy steps

- The API's `ForwardedHeaders:KnownProxies`/`KnownNetworks` must list the gateway before or with this
  rollout. Otherwise the rate limiter keys every client on the gateway address and all clients share one
  bucket.
- The gateway's `ForwardedHeaders:KnownProxies`/`KnownNetworks` must list the ingress before or with this
  rollout. The gateway strips the ingress's `X-Real-IP`, so an untrusted ingress leaves `RemoteIpAddress` as
  the ingress, YARP appends that address to `X-Forwarded-For`, and the API keys every client on the ingress.
- Compose publish sets no `ForwardedHeaders`, and since ADR 0112 passes no `SerilogStepUp__*` values. A
  compose deployment sets `ForwardedHeaders` itself, and sets `SerilogStepUp__Mode=Diagnostic` (with an
  optional `SerilogStepUp__DiagnosticDurationMinutes`) through the environment only while it needs
  diagnostic mode, then sets `SerilogStepUp__Mode` back to `Auto` after the session, because Diagnostic
  re-arms on every start. The other `SerilogStepUp` defaults ship in code (ADR 0112), so it does not set
  them.

### Consequences

- A loopback caller that needs a rate-limit exemption or a different limit is configured explicitly in the
  deployment. The API's `RemoteIpAddress` now comes from the gateway's `X-Forwarded-For`, so a loopback entry
  would match a client that claims 127.0.0.1 through the ingress.
- `EndpointWhitelist get:/api/*` exempts every GET from rate limiting, so the anonymous GET token routes rely
  on token entropy alone.
- The gateway's own IP checks see the client behind the ingress. `RoleManagementAuthMiddleware` only logs it.
  `ServiceCallerValidator.ValidateCaller` is registered but has no live caller; if it is wired in later,
  external callers stop passing `IsInternalIp`.
- Logs and request summaries now carry real client IPs.
- The audit `SourceIp` and the client IPs in logs are personal data. They are kept under the
  security-logging legitimate interest and exempt from erasure, with the archive's retention. That retention
  is `TODO(question)`, tracked as R-01 in [`07-risks.md`](../07-risks.md).

## Rejected

- StepUpLogging `TrustForwardedHeaders`: it changes only the logged IP, not the audit `SourceIp`, the rate
  limiter or the gateway's checks.
- Trusting every sender: any client could then set its own address.
- Keeping the gateway address in the audit record: it identifies no one.
- Truncating the IP in the audit column.
