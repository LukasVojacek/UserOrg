# ADR 0062 - OpenAPI schema reference IDs are qualified by the declaring endpoint

- Status: accepted

## Context

The v2 endpoint convention (CLAUDE.md, ADR 0020) requires every Minimal API endpoint to declare its
request/response DTOs as nested records literally named `Request`, `Response`, and (for a `[FromBody]`
sub-payload) `Body`:

```csharp
internal static class CreateProjectEndpoint
{
    internal sealed record Request(string ShortName, string Name, ...);
    internal sealed record Response(string ShortName);
}
```

`Microsoft.AspNetCore.OpenApi` (10.0.9, .NET 10) derives each component schema's reference id from the CLR
type's **short name** via `OpenApiOptions.CreateDefaultSchemaReferenceId` (≈ `type.Name`). Because dozens of
distinct nested types all have the short name `Request` / `Response` / `Body`, they all map to the same
`$ref` id — and the generator resolves the collision by **last-writer-wins overwrite**, silently, with no
warning.

Observed in the generated docs (verified live):
- `#/components/schemas/Response` is referenced by **32** different endpoints in `API_v2-user.json` alone;
  `Request` by 3; `Body` by 2.
- Consumers reading the docs get the **wrong** request/response body. `POST /api/v2/Projects` omits the
  required `ShortName` (a spec-following consumer gets HTTP 400); `POST /api/v2/Providers/{name}/Resources`
  shows provider fields instead of the resource body.
- Endpoints whose records are uniquely named (`GetProjectResponse`, `UserInfoResponse`, `ResourceResponse`)
  are unaffected — which pins the cause to short-name collision, not the convention itself.

The API runtime is correct; only the generated OpenAPI documentation is wrong. It is a contract-accuracy /
developer-experience defect. The collision is a property of the v2 endpoint convention (nested
`Request`/`Response`/`Body` records); the legacy v1 documents do not use that convention and are not affected.

## Decision

Set `OpenApiOptions.CreateSchemaReferenceId` in `ConfigureV2OpenApi`
(`src/Core/API/Composition/V2CoreExtensions.cs`) so it applies to the **v2 documents only** (`v2-User`,
`v2-ProviderAdmin`, `v2-Admin`). A **nested** type's schema id is qualified by its declaring type: the declaring
type's name with a trailing `Endpoint` stripped, concatenated with the type's own name. Top-level types (no
declaring type) keep the framework default.

The delegate is registered *after* the `ConfigureSharedOpenApi(options)` call inside `ConfigureV2OpenApi`, **not**
in `ConfigureSharedOpenApi` itself. The legacy v1 documents (`API_user.json`, `API_administration.json`) route
through `ConfigureLegacyOpenApi` → `ConfigureSharedOpenApi` only, so they deliberately stay on the framework
default schema id and remain **byte-stable**. Scoping the fix to v2 is both sufficient (the defect only ever
affected v2 docs) and minimal. A `LegacyOpenApiSchemaIdCharacterizationTests` safety net pins the original v1
nested schema ids (e.g. `DataStagingDataModel`, `DataStagingPart`) and asserts their endpoint-qualified forms
are absent, so a future leak of the delegate onto v1 turns the build red.

```
CreateProjectEndpoint.Request     -> CreateProjectRequest
UpdateProjectEndpoint.Request     -> UpdateProjectRequest
ChangeProjectUserRoleEndpoint.Body-> ChangeProjectUserRoleBody
AssignResourceEndpoint.SpecificationItem -> AssignResourceSpecificationItem
ProjectShortName (top-level VO)   -> (default, unchanged)
```

The convention (nested records named `Request`/`Response`) is preserved — the fix lives entirely in one
delegate, not in 76 record renames.

**Guardrail (the durability half).** The silent-overwrite failure mode is what makes this recurrent: a new
endpoint whose id the delegate does not disambiguate would collide again, unnoticed. So the fix ships with an
invariant test that builds each real generated v2 OpenAPI document and asserts **no operation references a
component schema that a different CLR type also produced** (equivalently: request/response schema count matches
the number of endpoints with bodies/typed responses). The test uses the real document (via
`WebApplicationFactory<Program>`), not a re-derivation of the delegate, so it catches convention drift and
delegate gaps alike. It turns a future collision from a silent doc corruption into a red build.

## Considered options

1. **Qualify by declaring endpoint, stripped `Endpoint` suffix (chosen).** Clean, readable schema names in
   Scalar; one delegate; convention untouched. Residual same-stripped-name collisions are caught by the
   guardrail rather than assumed away.
2. **Qualify with the full declaring type name** (`CreateProjectEndpointRequest`). Zero residual-collision risk
   but verbose, noisier in the UI. Rejected in favour of the cleaner names + guardrail.
3. **Namespace-qualified ids.** Bulletproof uniqueness, but long, ugly schema names. Rejected.
4. **Rename every `Request`/`Response` record to a unique name.** Fixes it but fights the mandated convention
   across 76 files and demands perpetual manual vigilance on every new endpoint. Rejected.
5. **Snapshot the whole OpenAPI JSON in a test.** Would catch everything but goes red on every legitimate
   endpoint addition — churn the team learns to blindly re-baseline. Rejected in favour of the targeted
   collision invariant.

## Consequences

- Only the three v2 `API_v2-*.json` artifacts regenerate with new schema ids when `src/Core/API` builds; the
  diff is large but purely documentation (schema renames + correct per-endpoint bodies) and carries no runtime
  contract change. The two legacy `API_user.json` / `API_administration.json` documents are **unchanged**
  (byte-stable) because the delegate is v2-only. The json regeneration is committed separately from the logic.
- Scalar renders correct, per-endpoint request and response bodies. `POST /api/v2/Projects` shows `ShortName`.
- Adding a new endpoint keeps the nested `Request`/`Response` convention; the delegate names its schemas
  uniquely and the guardrail test fails loudly if it ever cannot.
- The delegate is convention-dependent (keys off `…Endpoint.Xxx`); the guardrail is what protects against the
  convention drifting, so the two ship together — neither alone is sufficient.
