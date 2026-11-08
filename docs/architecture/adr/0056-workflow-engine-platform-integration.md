# WorkflowEngine integrated into the platform (shared DB, tool-applied migrations, logging callback)

## Context

Issue #215 (MR !276) added a `CreateResourceOnLocation` endpoint that submits an **async**
resource-creation workflow through `IRequestApplicationService.SubmitAsync` (steps:
CheckHeappe → InitializeProjectResource → InitializeHeappe → Finalize → SendEmail). `CheckHeappe`
is the flow's pre-flight gate (HEAppE liveness + allocation-unit-type existence) and runs **before**
`InitializeProjectResource` creates the `ProjectResource` row — see T-005-01 / ADR-0057 for why the
gate must not touch persistence itself. But the
`WorkflowEngine.Core` backbone it depends on was only ever wired in the throwaway
`WorkflowEngine.Sample` project — never in the platform. Verified consequences:

- `AddWorkflowEngine()` is never called in `src/Core/API/Program.cs`; `AddCore` only calls
  `AddCoreResourceWorkflow()` (strategy + step handlers), so `IRequestApplicationService` — injected
  into the endpoint — fails to resolve at runtime.
- No `ConnectionStrings__WorkflowDBContext`; `src/UserOrg.AppHost/Program.cs` provisions no
  workflow database; `WorkflowDbContext` (schema `workflow`) has **no** EF migrations.
- The only `ICallbackPort` implementation is Sample's `ConsoleCallbackAdapter`. `AddWorkflowEngine`
  does **not** register an `ICallbackPort`; `RequestWorker` requires one
  (`ArgumentNullException.ThrowIfNull`), so worker construction throws without a host-supplied adapter.

## Decision

Integrate `WorkflowEngine.Core` into the real host, reusing the platform's established patterns:

1. **Registration.** Call `builder.AddWorkflowEngine()` in `src/Core/API/Program.cs` (the host, per
   the architecture standard §4 — the only project that may wire framework/persistence). It stays
   out of `AddCore` because engine persistence is a host concern, not a Core-module concern.
2. **Database — share `UserOrgDBContext`, isolate by schema.** `ConnectionStrings__WorkflowDBContext`
   points at the **same** `userorgDatabase` handle already used for `UserOrgDBContext` and
   `ForgeDBContext`; isolation is the existing `workflow` EF schema. This mirrors how Forge, Outbox,
   and Heappe already share one physical database. No new `AddDatabase` resource.
3. **Migrations — via the canonical `DatabaseMigrations` tool.** Register `WorkflowDbContext` in
   `src/Tools/DatabaseMigrations/Program.cs`, add it to `GetAllDbContextTypes()` and the
   `contextProjects` map in `MigrationRunner.cs`, and pass `ConnectionStrings__WorkflowDBContext` to
   the `db-migrations-apply` executable in `MigrationToolExtssensions.cs`. Scaffold the **initial**
   `WorkflowDbContext` migration (the `workflow`-schema tables) as its own migration set. Startup is
   already gated on the tool via `WaitForCompletion(migrationApply)`, so the API never starts against
   an unmigrated `workflow` schema.
4. **Callback — logging adapter now, transport deferred.** Register a minimal platform
   `ICallbackPort` that logs the terminal `RequestAggregate` (Completed/Failed). Real outbound
   notification transport (email/webhook) is out of scope and tracked with the email body under
   **#239**. The endpoint stops passing the meaningless `callbackUrl: "console://notifications"`
   (nothing parsed it); terminal observability comes from the logging adapter plus engine telemetry.

## Consequences

- The feature runs end-to-end: submit resolves, the worker dispatches all steps, and terminal state
  is logged. No new database, backup, or snapshot wiring — one connection-name alias + one migration
  registration + one initial migration.
- `WorkflowDbContext` joins the same startup-gated migration path as every other context; a pending
  `workflow` migration blocks API start exactly like the others, not silently.
- The logging callback is a deliberate MVP. When #239 lands real notifications, the adapter is the
  single seam to swap — the engine contract (`ICallbackPort.SendAsync(RequestAggregate)`) does not change.
- Sharing the DB means a `workflow`-schema migration ships in the same tool run as core migrations;
  the schema isolation keeps them independent despite the shared connection.
