# ADR 0093 - Log the token id in every component

- Date: 2026-08-05
- Status: accepted

## Decision

Both processes (Gateway and Core API) log the chain value `original_jti ?? jti` per ADR 0092:

1. **A log event at token acceptance/validation**, from the `OnTokenValidated` handlers that already
   exist and already resolve a logger (Gateway `ServiceExtensions.cs:301`, Core API
   `AuthenticationExtensions.cs:109`), naming the jti of the token just accepted. Both run inside the
   ASP.NET Core request Activity, so OpenTelemetry stamps TraceId/SpanId onto the record — the jti log
   is findable for a given trace without any additional correlation plumbing.
2. **`IDiagnosticContext.Set("auth.jti", …)`** so the value lands on the StepUp request-summary
   record — the one record per request that satisfies "each component logs the token ID".
   `Lukdrasil.StepUpLogging` wraps Serilog's `UseSerilogRequestLogging` and `IDiagnosticContext` is
   its supported enrichment channel into that event; both hosts get the singleton via
   `AddServiceDefaults` → `AddStepUpLogging` (src/UserOrg.ServiceDefaults/Extensions.cs:37, wired at
   Gateway Program.cs:36 and Core API `src/Core/API/Composition/HostInfrastructureExtensions.cs:120`).
3. **`auth.jti` Activity tag**, next to the existing `auth.*` tags in both `AuthTelemetry` classes,
   so the value is queryable in traces alongside `auth.user_id`.
4. All three are resolved **after authentication** — before it there is no principal and therefore
   no token id.

## Amendment (2026-09-30)

- Decision item 1: the acceptance event (`Token accepted with jti`) is logged at **Debug** from ADR 0112 on,
  so it is emitted only in diagnostic mode. The request summary's `auth.jti` (item 2) and the `auth.jti`
  Activity tag (item 3) are the record of truth in every mode.
- The `auth.path` tag listed under Context is **dropped** from both `AuthTelemetry` classes: it carried the
  raw request path, which holds a credential on the public token routes. The parent HTTP span's `url.path`,
  redacted per ADR 0112, carries the path.

## Amendment (2026-10-12): the raw jti stays at Information (#335)

The raw `jti` stays in the request summary and the `auth.jti` Activity tag at Information. It is an opaque
random identifier, not a credential, and it is not part of any audit record, so logging it does not widen what
a log reader can do. The #335 criterion to hash or drop it is overridden by this decision.

## Context

ADR 0092 makes the original token id available inside the internal token. The platform requirement is
that each component **logs** the token id of the token that arrived, so the chain can later prove the
user wanted the action.

Nothing in UserOrg logs or traces a token id today: repo-wide, `jti` appears only at the two mint
sites in `TokenService.cs` (:78, :187). `AuthTelemetry` carries
`auth.path/authority/scheme/has_authorization/user_id/error/exception` — no token id.

An earlier draft of this ADR also returned the value to the client in a `jti` response header,
mirroring `X-Trace-Id`. That is withdrawn — the requirement is about logging, and a response header
is a separate, externally-visible API surface that nothing actually asked for.

## Consequences

- The token id never leaves the system: it appears in logs and traces, not in any response. No new
  API surface, no client-visible contract, nothing for a consumer to depend on.
- The chain is joinable in log/trace queries on `auth.jti` across components.
- A request rejected before authentication (401 on an invalid token) has no token id and simply logs
  none.
- `auth.jti` is **not** a request correlation id: one access token serves many requests, so the same
  value legitimately appears on many records. Request correlation stays with `traceparent` /
  `X-Trace-Id`; the two must not be conflated in dashboards.
- This ADR and ADR-0092 originated on `main` as `docs/adr/0051`/`0052`; they first landed here as
  `0088`/`0089`, then moved to `0092`/`0093` when a parallel merge landed an unrelated
  `0088-audit-log-poc-narrows-adr-0004.md` first — the numbers `0051`/`0052` and `0088`/`0089` in
  this directory (`docs/architecture/adr/`) are already live, unrelated decisions, so citing
  "ADR 0051"/"ADR 0052" or "ADR 0088"/"ADR 0089" for this decision on this branch is wrong.

## Rejected alternatives

- **A `jti` response header** (the earlier form of this ADR). Would have made the value visible to
  support without log access, but it is a public API surface added for an internal audit
  requirement, and it dragged in real complexity: `Response.OnStarting` plumbing in two processes to
  survive `Response.HasStarted` on proxied and short-circuited responses, plus coverage of the
  gateway-local endpoints. Withdrawn by the developer once the requirement was re-read as
  logging-only.
- **`BeginScope` in the auth handlers** (src/Core/API/Authentication/AuthenticationExtensions.cs:88-140).
  Scopes die with the handler; the request's own records would not carry the value.
- **A `LogContext` push from a middleware after `UseStepUpRequestLogging()`.** `Lukdrasil.StepUpLogging`
  wraps `UseSerilogRequestLogging`, so such a middleware is *nested inside* it and the push is
  disposed before the request-summary event is written.
