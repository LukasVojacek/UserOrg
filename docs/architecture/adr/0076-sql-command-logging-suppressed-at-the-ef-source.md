# 76. SQL command logging is suppressed at the EF source, not by a log-level filter

Date: 2026-07-10

## Status

Accepted

## Context

Issue [#241] ("Disable logging of SQL commands on production") and its duplicate [#235]
report that EF Core emits full SQL statements into production logs.

EF Core logs executed SQL through `RelationalEventId.CommandExecuted` at `LogLevel.Information`.
Three facts about this repository make the obvious fix — pinning the
`Microsoft.EntityFrameworkCore.Database.Command` category to `Warning` in `appsettings.json` —
insufficient:

1. **The Gateway has no such override at all.** `src/Modules/Services/Gateway/UserOrg.Gateway/appsettings.json`
   sets `Logging:LogLevel:Default = Information` and carries no `Microsoft.EntityFrameworkCore*`
   entry, so EF SQL is logged in every environment. This is the live production leak.
2. **A static level filter is defeated by step-up logging.** `AddServiceDefaults` wires
   `Lukdrasil.StepUpLogging` into both hosts (`src/UserOrg.ServiceDefaults/Extensions.cs:37`).
   The Gateway's `SerilogStepUp:StepUpLevel` is `Information`
   (`.../UserOrg.Gateway/appsettings.json`). When a request is stepped up, the effective
   minimum level drops to `Information` and the SQL reappears — regardless of any
   `MinimumLevel.Override` or `Logging:LogLevel` entry.
3. **There is more than one logger pipeline.** The API runs Serilog with
   `writeToProviders: true`; the Gateway runs plain `Microsoft.Extensions.Logging`; both feed
   OpenTelemetry. A filter must be repeated in each pipeline and re-checked whenever one is added.

The API's real `appsettings.json` is git-ignored and mounted from `/opt/API/confs/appsettings.json`
in Docker, so a committed config change cannot even be relied on to reach production.

## Decision

Suppress the SQL command log **events themselves** at the `DbContext` registration, so no
logger ever sees them:

```csharp
options.ConfigureWarnings(w => w.Ignore(
    RelationalEventId.CommandExecuting,
    RelationalEventId.CommandExecuted));
```

applied whenever the host is **not** in the `Development` environment.

- A single extension method, `ConfigureSqlCommandLogging(bool enabled)`, lives in
  `src/UserOrg.Shared/Persistance/` and is called from every request-serving DbContext
  registration site allowed to reference `UserOrg.Shared` (`Forge.Core`,
  `Infrastructure.DataAccess` via `Application`, `Heappe`, the modular Core.DataAccess
  `DatabaseContext`, `OutboxDbContext`, `ResourceRequestsDbContext`). This is **not** every
  DbContext registration site in the repo — see the next bullet for the one that must not take
  that reference, and the Consequences section for why `src/Tools/DatabaseMigrations` needs no
  such call either.
- `WorkflowEngine.Core`'s `WorkflowDbContext` is the one exception: it inlines the same
  `ConfigureWarnings(w => w.Ignore(RelationalEventId.CommandExecuting, RelationalEventId.CommandExecuted))`
  call instead of referencing `ConfigureSqlCommandLogging`, because ADR-0013 keeps
  `WorkflowEngine.Core` domain-agnostic, standalone and NuGet-publishable — it may not take a
  `ProjectReference` to `UserOrg.Shared`. See also ADR-0085 (the `IEmailSender` port), which
  makes the same UserOrg.Shared-vs-Core placement tradeoff for a different cross-cutting concern.
- The on/off property required by [#241] is `ASPNETCORE_ENVIRONMENT`. No new configuration key
  is introduced.
- `RelationalEventId.CommandError` is **left enabled**. Failing commands are rare and their SQL
  is the primary diagnostic for a production incident; parameter values stay redacted because
  `EnableSensitiveDataLogging` is already gated on `IsDevelopment()`.

## Consequences

- Suppression is total and cheap. EF never formats the SQL string, so neither a Serilog level
  override, nor a StepUpLogging step-up, nor a future log sink can resurrect it. It also removes
  the per-command message-formatting cost in production.
- The fix is not implemented inside `Lukdrasil.StepUpLogging`. That package can only *filter*
  records EF has already produced; it would need EF-category knowledge it has no business
  holding, it would not cover the non-Serilog Gateway pipeline or the OTEL path, and shipping it
  would gate this hotfix on an external NuGet release. A separate follow-up in that repository
  should add a never-step-up category exclusion list as defense-in-depth for its other consumers.
- `ConfigureWarnings` can cause EF to build a new internal service provider. The same
  configuration is applied at every registration site, so at most one extra provider is built.
- Turning SQL logging back on in production for incident debugging now requires setting
  `ASPNETCORE_ENVIRONMENT=Development`, which changes other behaviour too. If a narrower escape
  hatch is ever needed, add a `Logging:SqlCommands:Enabled` (`bool?`, defaulting to
  `IsDevelopment()`) and pass it to the same extension method — the seam already exists.
- OpenTelemetry's second SQL-exposure path (`AddSqlClientInstrumentation` with
  `includeDbStatements: builder.Environment.IsDevelopment()`,
  `src/UserOrg.ServiceDefaults/Extensions.cs:68`) is already environment-gated and is out of
  scope for this hotfix.
- `src/Tools/DatabaseMigrations` calls `builder.Logging.ClearProviders()` and needs no change.

[#235]: https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/235
[#241]: https://opencode.it4i.eu/lexis-platform/backend/userorg/-/issues/241

## Amendment (2026-09-30, ADR 0112)

Context item 2 no longer holds: the framework categories are held by StepUpLogging category floors and Serilog root overrides that stay in force during step-up, and the EF SQL categories stay at Warning root overrides in every mode. Context item 3 is superseded: the API's UseSerilog(writeToProviders: true) block is removed, and both hosts reach sinks and OTLP through the StepUpLogging pipeline. Item 1's Gateway StepUpLevel block is gone, and the defaults are in code. The decision is unchanged.
