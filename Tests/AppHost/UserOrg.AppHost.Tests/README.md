# UserOrg.AppHost.Tests

Composition tests for `src/UserOrg.AppHost`, asserted over the AppHost's **source syntax** rather than
over a built resource model.

## Why syntax and not the model

The AppHost is an executable whose whole composition lives in the top-level statements of `Program.cs`
and ends in `await builder.Build().RunAsync()`. There is no seam that hands the resource model to a
caller, and [ADR-0069](../../../docs/architecture/adr/0069-testcontainers-only-e2e-retire-aspire-boot.md)
retired the `DistributedApplicationTestingBuilder` boot path from this repository — booting the AppHost
to inspect its model is exactly the mechanism that decision removed. ADR-0069 also records the
consequence in as many words: *"AppHost-composition regression coverage is dropped with no direct
replacement."*

These tests are a partial replacement that stays inside that decision. Roslyn parses the AppHost's own
`.cs` files (the same tool `Tests/UserOrg.Shared.Hashing.Tests` already uses) and the assertions are
written in the vocabulary the AppHost's bugs live in:

- **which resources are registered** — `AddSqlServer("userorgDatabase")`, `AddDatabase("UserOrgDBContext")`, …
- **whether a registration is decided at model-build time** — `AppHostSyntax.BuildTimeConditions`
  returns the `if` conditions that gate a node, anchoring at the *outermost enclosing lambda*: an `if`
  inside a callback is a run-time decision, an `if` around the callback's registration is not.
- **whether a value is produced at model-build time or inside a resource-start callback** — a
  two-argument `WithEnvironment("Key", value)` versus `WithEnvironment(async ctx => …)`.

## What they cannot tell you

They pin the *mechanism*, never the run. Whether the dashboard's unresolved-parameter dialog actually
drives the topology, whether the SQL chain starts in the right order against the
`WaitForCompletion(db-migrations-apply)` edges, and whether *Save to user secret* persists the answer,
can only be shown by running `aspire run` — as T-071's `## Risks` R1 says.

`Should_ParseEveryAppHostSourceFileWithoutSyntaxErrors` is the guard that keeps the negative assertions
honest: if the AppHost ever uses syntax this Roslyn version cannot read, every "must not appear" test
would go vacuously green, so that one goes red instead. It is also why `AppHostSource.Parse` sets
`LanguageVersion.Preview` — the AppHost uses C# 14 `extension(…)` blocks.

## Layout

| file | contents |
|---|---|
| `AppHostSource.cs` | locates and parses the AppHost's source files |
| `AppHostSyntax.cs` | fluent-chain lookup and the build-time/run-time distinction |
| `AppHostCompositionCharacterizationTests.cs` | today's composition, the parts that must survive |
| `DatabaseSourceLateResolutionTests.cs` | T-071-01 (E1): the database source resolved at run time |
| `ResourceCommandSyntax.cs` | lookup for the `WithCommand` registrations and their `CommandOptions` |
| `DatabaseCommandCharacterizationTests.cs` | today's SQL commands and the seed executable, the parts that must survive |
| `DatabaseLifecycleCommandTests.cs` | T-071-03 (E3, E9): the `db-lifecycle` command and the progress dialogs |
