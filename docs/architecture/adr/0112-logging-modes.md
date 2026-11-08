# ADR 0112 - Logging modes: Warning normally, Information on step-up, time-limited diagnostic Debug

- Date: 2026-09-30
- Status: accepted, amended 2026-09-30 for Lukdrasil.StepUpLogging 5.0.2 and 2026-10-12 for #334 and R-02 (see Amendments)
- Relates: ADR-0076 (narrows context item 2, supersedes context item 3),
  `adr/0058-waldur-logging-contract-stepuplogging.md` (the Waldur level contract, unchanged), ADR-0093
  (its acceptance event is Debug from here on), ADR 0114 (client IPs in logs)

## Context

Both hosts log through Lukdrasil.StepUpLogging 4.0.0. Before this ADR the Gateway's committed
`appsettings.json` stepped up to Information for 180 s with request bodies captured, the AppHost stepped up to
Debug, and the framework categories (Polly, HttpClient, YARP, EF Core, ASP.NET Core) followed the level switch,
so every step-up exported framework noise at Information (#334, #336). The API's `appsettings.json` is
git-ignored and mounted in deployments (ADR-0076:32-34), so a committed file cannot carry production values.

StepUpLogging 4.0.0 has no per-category floor and no diagnostic duration. It exposes
`StepUpLoggingController.LevelSwitch` and honours reloadable `Serilog:MinimumLevel:Override` entries, and in
`AlwaysOn` mode it ignores step-up triggers and `NeverStepUpCategories`.

## Amendment (2026-09-30): StepUpLogging 5.0.2

Both hosts now log through Lukdrasil.StepUpLogging 5.0.2, which ships what the 4.0.0 workarounds stood in
for: `CategoryFloors`, `Mode=Diagnostic` with `DiagnosticDurationMinutes`, `AlwaysExportCategories` (#35),
redaction of the summary path, the hosting scope's `RequestPath` and `http.target` (#30, #66), a property
sweep that covers `PathString` arguments (#34), and an audit sink that strips inherited resilience
handlers. The sections below are amended in place: `DiagnosticModeGuard`, `DiagnosticLogging:*`, the
`IsImmediate` enricher and the token-route ExcludePaths are gone, and the pre-error buffer is on. The
framework categories are category floors; the exporter and lifecycle categories stay Serilog root overrides
(the later amendment removes the diagnostic-exempt root overrides). Rejected: all root overrides (Diagnostic
could not lift them); all floors with `DiagnosticExemptCategories` (EF SQL and raw query strings would be buffered
and exported on any Error, until `PreErrorBufferExcludeCategories` made the exemption redundant);
keeping the guard's BaseLevel-until-restart after the diagnostic window (it fights the library controller
over the level switch).

## Amendment (2026-10-12): the Error flush exports no framework Information (#334) and what stays (R-02)

- StepUpLogging 5.3.0 is the package version in use. Its `PreErrorBufferExcludeCategories` option keeps events
  below Warning from the listed categories out of the pre-error buffer (StepUpLogging#79, Lukdrasil/StepUpLogging#80).
  `ProductionLoggingDefaults` sets it to `FrameworkCategories` plus the EF `Database.Command`, `ChangeTracking`,
  `Query` and `Update` categories and `Microsoft.AspNetCore.Hosting.Diagnostics`, so an Error no longer exports the
  framework Information of its trace. The #334 criterion is **met**; application Information is still held and
  flushed. A mounted file or environment variable overrides the list like any other default.
- `FrameworkLevelOverrides.DiagnosticExemptCategories` is **removed**: the buffer exclusion makes the root
  override redundant for Normal and Auto mode. The EF and `Hosting.Diagnostics` categories are now ordinary
  framework categories, under the `Microsoft.EntityFrameworkCore` floor (the EF ones) and the `Microsoft` floor
  (`Hosting.Diagnostics`).
- Diagnostic mode therefore lifts EF `Database.Command`, `ChangeTracking`, `Query` and `Update` and
  `Hosting.Diagnostics` to Debug, and an Error in Diagnostic mode exports them at Debug. SQL parameters and
  entity values can then reach OTLP. The person accepted this privacy risk because Diagnostic is manual and
  time-limited and redaction still applies.
- `ClientIp` and `UserAgent` stay in the request summary (R-02); their retention is decided separately.

## Decision

**Three logging modes, one request summary per request, and the production values in code.**

### Modes

- **Normal mode**: the level switch at BaseLevel Warning, the framework categories at their Warning
  category floor.
- **Auto step-up**: after an Error the switch rises to Information for DurationSeconds 60, bounded by
  MaxContinuousStepUpSeconds 600 with StepUpCooldownSeconds 300. `TreatServerErrorStatusAsError=false`.
  The framework categories stay at their Warning floor and the switch never reaches Debug.
- **Diagnostic mode**: the only state that emits Debug. The library's `SerilogStepUp:Mode=Diagnostic`
  turns it on at the next start; `SerilogStepUp:DiagnosticDurationMinutes` (library-validated 1 to 120,
  default 30) bounds it. The switch sits at DiagnosticLevel Debug and the library lifts the category floors;
  root overrides are not lifted. The library logs a Warning at entry and one at expiry, and after expiry
  the host runs as Auto at BaseLevel with auto step-up working. `SerilogStepUp:Mode` is an ordinary key with
  no precedence (Defaults in code). The mode re-arms on every start while `Mode=Diagnostic` is set, so
  **the operator sets `SerilogMode` back to Auto after the session**. The AppHost exposes it as the
  `Diagnostic` option of the `SerilogMode` parameter.

### Categories

Held by an in-memory configuration source added last (`FrameworkLevelOverrides.AddFrameworkLevelOverrides`,
`src/UserOrg.ServiceDefaults/Logging/FrameworkLevelOverrides.cs`), so appsettings and environment variables
cannot override the same key; a more specific `SerilogStepUp:CategoryFloors:<category>` key from any
configuration source still wins by prefix and lowers the floor for that category. It sets a **category floor**
(`SerilogStepUp:CategoryFloors:<category>`) or a **root override** (`Serilog:MinimumLevel:Override:<category>`);
the longest matching prefix picks the entry in both. An event below a floor is not exported but held in the
pre-error buffer, and Diagnostic mode lifts the floor. An event below a root override is dropped before any
sink, so it never reaches the buffer, in every mode.

- **Framework categories**, a category floor at Warning, lifted in diagnostic mode: `Polly`, `System.Net.Http.HttpClient`,
  `Microsoft.AspNetCore.Routing`, `.StaticFiles`, `.Server.Kestrel`, `.Http.Result`, `.Authentication`,
  `.Authorization`, `Microsoft.EntityFrameworkCore`, `Yarp.ReverseProxy`,
  `Serilog.AspNetCore.RequestLoggingMiddleware`, `Duende`, `AspNetCoreRateLimit`, `Ogham`, and the
  catch-alls `Microsoft` and `System`.
- **Pre-error buffer exclusions**, `SerilogStepUp:PreErrorBufferExcludeCategories`: the framework categories above
  plus `Microsoft.EntityFrameworkCore.Database.Command`, `.ChangeTracking`, `.Query`, `.Update` and
  `Microsoft.AspNetCore.Hosting.Diagnostics`. Their events below Warning are never held, so an Error flush does not
  export them; Diagnostic mode still exports them at Debug.
- **Exporter categories**, a root override at Warning in every mode: `System.Net.Http.HttpClient.OtlpLogExporter`,
  `.OtlpMetricExporter` and `.OtlpTraceExporter`.
- **Lifecycle category** `Microsoft.Hosting.Lifetime`, a root override at Information in every mode, and
  listed in `SerilogStepUp:AlwaysExportCategories`, so the library marks its events `IsImmediate`: they pass
  the level switch and the `Microsoft` floor, are exported once and are never held. This replaces the
  `IsImmediate` enricher of the 4.0.0 wiring.

This narrows ADR-0076 context item 2: the floors and root overrides hold under step-up.

### Request summary

StepUpLogging's `IsRequestSummary` event (`AlwaysLogRequestSummary=true`) is written through the bypass logger,
once per request, in every mode, the public token routes included (Redaction below). It carries `ClientIp` and `UserAgent`, which ADR 0114 makes the real client's,
and the non-credential query values, which flushed Yarp and HttpClient events carry too.
Legal basis: the security-logging legitimate interest. Retention: `TODO(question)`, risk
[R-02](../07-risks.md). Omitting IP and UA below status 400 needs a StepUpLogging option and is a package
follow-up. The Serilog completion event (`HTTP ... responded`) is a framework category.

### Defaults in code

`ProductionLoggingDefaults.AddProductionLoggingDefaults` inserts the values as the first, lowest-priority
configuration source in both hosts: BaseLevel Warning, StepUpLevel Information, DurationSeconds 60,
MaxContinuousStepUpSeconds 600, StepUpCooldownSeconds 300, TreatServerErrorStatusAsError false,
AlwaysLogRequestSummary true, CaptureRequestBody false, EnablePreErrorBuffering true, PreErrorBufferSize 50,
PreErrorMaxContexts 256, ExcludePaths `/health`, `/alive`, `/metrics`, `/api/scalar.js`,
`/api/scalar.aspnetcore.js`. A mounted `appsettings.json` or a `SerilogStepUp__*` variable still overrides
them; only the category floors and root overrides sit above both. `SerilogStepUp:Mode` is an ordinary key
with no precedence: on the API a mounted `appsettings.json` is added after the environment (`Program.cs`,
the `AddJsonFile("/opt/API/confs/appsettings.json", ...)` call), so a `Mode` it carries overrides
`SerilogStepUp__Mode=Diagnostic`. The API inserts them before its Vault source,
because inserting a source rebuilds every provider already added; AddServiceDefaults does not insert them twice.

**The pre-error buffer is on**, with userorg's own values instead of the library defaults: at most 50 held
events per trace and 256 traces, LRU (about 13 MB worst case). It keeps Normal mode quiet while an Error
carries its context: an Error or Fatal in a trace exports that trace's held events exactly once, and host
dispose exports none. Framework categories are excluded from the buffer, so no framework Information below
Warning is exported by a flush (#334). An Error burst over N traces exports up
to 50·N held events. The root overrides drop the exporter categories before the buffer, and the exclusion keeps
the framework categories, SQL parameters and raw query strings included, out of a flush in Normal and Auto mode. The buffer has no age option; a
trace's buffer ends on flush, LRU eviction or dispose. The AppHost wires its logging parameters in
run mode only, prefilled with the same values.

Configuration arrays merge by index, and a Serilog root override on a framework category acts before the
category floor in every mode and Diagnostic mode cannot lift it, so
**deployments remove these keys from mounted `appsettings.json` files**:

- any `SerilogStepUp` value above, including a shorter `ExcludePaths`, `EnablePreErrorBuffering` and the
  former token-route `ExcludePaths` entries
- any `SerilogStepUp:CategoryFloors` entry, because a more specific key wins by prefix over the floors above
- `SerilogStepUp:Mode`, which would override `SerilogStepUp__Mode=Diagnostic` on the API
- the `DiagnosticLogging` section, which nothing reads any more
- `Serilog:MinimumLevel:Default` at Information
- any `Serilog:MinimumLevel:Override` for a framework category, at any level, other than the
  exporter and lifecycle categories (Diagnostic mode cannot lift a root override)
- `Logging:LogLevel`, which has no effect under Serilog

and **add** `"shared": true` to a File sink, because the gated and the bypass logger both write to it
(StepUpLogging README:1035).

### One pipeline

The API's `ClearProviders`/`UseSerilog(writeToProviders: true)` block is gone. StepUpLogging's own `AddSerilog`
already owned `ILoggerFactory`, so the block was dead; both hosts reach the config-declared sinks and OTLP
through the StepUpLogging pipeline (`ApiOtlpLogPathTests`). This supersedes ADR-0076 context item 3.

### Redaction

`LogRedactionDefaults.Apply` (`src/UserOrg.ServiceDefaults/Logging/LogRedactionDefaults.cs`) runs as a
`PostConfigure<StepUpLoggingOptions>` in both hosts and assigns `RedactLogEventProperties=true`, the
`RedactionRegexes` and the `AdditionalSensitiveHeaders` wholesale, so no mounted file or variable can weaken
them. The patterns cover credential-named JSON members and form fields, the role-management console `t=`
parameter in any casing or percent-encoding, and the public token path segment in any casing. The headers are
`x-original-authorization`, `x-api-servicetoken`, `x-api-providerservicekey`, `x-api-token` and `x-api-key`,
next to StepUpLogging's built-in list. Claim dumps stay at Debug and go through `ClaimLogFormatter`, an
allow-list that prints every other claim type as `[REDACTED]`. Since StepUpLogging 5.0.2 the property sweep
also covers `PathString`, `QueryString`, `HostString` and `Uri` arguments (#34). It does not reach
message-template text, so the `PathStringLogGuard` test over `src` is narrowed to request path, path base and
query values interpolated into log message templates.

StepUpLogging 5.0.2 redacts the summary's path, the `RequestPath` the hosting log scope attaches to every
event of a request and the `http.target` span attribute with `RedactionRegexes` (#30, #66), so the public
token routes are no longer in ExcludePaths and `LogRedactionDefaults.Apply` no longer touches ExcludePaths.
Those requests write a request summary with the token segment redacted and take part in the 4xx/5xx
step-up trigger. Removing the token routes from ExcludePaths is safe only on 5.0.2 or later.

### Traces

The ASP.NET Core and HttpClient instrumentation rewrite `url.path` and `url.full` with the public token path
pattern of `LogRedactionDefaults`, so the token segment never reaches a span.

The OpenTelemetry ASP.NET Core and HttpClient instrumentation redact every query value by default.
`OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` and
`OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION` are never set.

Rejected: a config-registered filter (it needs a static bridge to the LevelSwitch); static Warning
overrides (diagnostic mode could not lift them); waiting for a StepUpLogging release; a runtime switch into
diagnostic mode (out of scope).

## Quality scenarios

The table is the 2026-09-30 amendment's. QS-04's stimuli are those of the original QS-04: a JSON body with
password or token, a form body, a token with PII claims, Authorization / X-Original-Authorization /
X-Api-* headers, the public token paths in any casing, `/role-management?t=`, EF queries over users.

| id | Attribute | Source and stimulus | Environment | Response | Measure |
|---|---|---|---|---|---|
| QS-01 | Operability (log volume) | v2 user endpoint sweep, about 130 requests with no Error | Normal mode, production logging defaults | one request summary per request, public token routes included; framework categories silent below Warning; on an Error, framework records below Warning are exported only by that trace's buffer flush (QS-05 bound), which replaces the original QS-01's auto step-up clause | ≤ 1 Information record per request; 0 framework-category records below Warning except the lifecycle category (≤ 4 plus 1 per listening address per process start) |
| QS-02 | Operability (step-up bound) | an Error every 10 s | Auto step-up | step-up ends while errors continue; never Debug | ends within 600 s; 0 Debug records |
| QS-03 | Operability (diagnostic bound) | `SerilogStepUp:Mode=Diagnostic` at process start | Diagnostic mode | the library's Warning on entry and on exit, then Auto at BaseLevel with step-up working | 1 Warning at entry, 1 at expiry; back to Auto within 30 min; an Error after expiry steps up within 1 s |
| QS-04 | Security (privacy) | the original QS-04 stimuli, plus an Error in a request that ran EF queries, was forwarded by the Gateway and carried a query string and a public token path | Normal and Auto mode, pre-error buffer on | framework categories, the EF and Hosting.Diagnostics ones included, are kept out of the buffer in Normal and Auto mode; summary path, RequestPath, http.target and the Yarp/HttpClient URLs exported in Diagnostic mode have credential, `t=` and token-path values redacted; other query values are exported under R-02 | the original QS-04 measure (0 non-allow-listed claim values, 0 unredacted password or token values, 0 credential header values, 0 public-route or console tokens), plus 0 SQL parameter values, 0 framework events below Warning in exported events in Normal and Auto mode, and 0 framework events in any buffer flush |
| QS-05 | Operability (error context) | an Error in a request after N held Information events | Normal mode | the trace's held events are exported once; dispose exports none | min(N, 50) events exported exactly 1 time; 0 on host dispose |
| QS-06 | Resource use (buffer memory) | 1000 concurrent traces | Normal mode | LRU eviction bounds the buffer; an Error burst over N traces exports up to 50·N held events | ≤ 256 traces, ≤ 12,800 held events, < 32 MB allocated-memory growth over the idle baseline |
| QS-07 | Reliability (audit delivery) | the audit endpoint does not answer | any mode, default `EncryptedSpool:DeliveryTimeout` | the record stays spooled; no step-up triggered; no resilience handler on `Lukdrasil.StepUpLogging.Audit` in userorg's registration order | attempt cut at 10 s; 1 POST per drain attempt; 0 Error records per failed attempt |

QS-04 is met by `LogRedactionDefaults` (T-USR-3-04). Request bodies are still not captured
(`CaptureRequestBody=false`); the body patterns guard the diagnostic-mode Yarp and HttpClient events and any
future capture.

## Consequences

- Normal-mode logs hold Warnings, one summary per request and the lifecycle lines.
- The public token routes write a request summary with the token segment redacted and can trigger step-up
  (Redaction above).
- An Error exports up to 50 held events of its trace, application events only: framework categories are
  excluded from the buffer (`PreErrorBufferExcludeCategories`); an Error burst over N traces exports up to
  50·N.
- A Debug session costs a restart to enter; after expiry the host runs as Auto with step-up working, and
  `SerilogMode` has to be set back to Auto, or the next start re-arms Diagnostic.
- Deployments must clean their mounted `appsettings.json` as listed above, or the defaults do not apply.
