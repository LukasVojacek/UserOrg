# ADR-0051 — E2E external-service faking via WireMock, redirected by seed data

- **Status:** Proposed
- **Date:** 2026-07-02
- **Context:** v2 Gateway endpoint integration-test coverage
- **Supersedes:** —

## Context

The E2E suite (`tests/E2E/EndpointsTests`) runs the **real** application
out-of-process through the Gateway via `DistributedApplicationTestingBuilder`. Two
v2 endpoints call HEAppE over HTTP (`GetProjectTemplates`, `CreateResourceOnLocation`)
and are therefore un-testable without contacting a real HEAppE instance.

Because the app runs as real processes, we **cannot** swap `IHeappeService` in-process
the way unit tests do with `FakeHeappeService` — there is no DI seam to reach into.
The only interception point is the **HTTP wire**.

Two facts make this cheap:
1. The HEAppE base URL is **data, not configuration**: it is read from DB rows
   (`AssignedLocationValue.Value` where `Name="HEAPPE_URL"`, fallback
   `Location.DefaultServiceIntegrationUrl`) and baked per-call into
   `BaseHeappeConnectionInfo.BaseAddress`. The HttpClient has no fixed base address.
2. The fixture already substitutes runtime placeholders in `seed-data.sql`
   (`__PROJECT_OWNER_IDP_ID__`, …).

## Decision

Add a **test-only HTTP fake** for HEAppE using **`WireMock.Net.Aspire`** (the
first-party WireMock.Net Aspire hosting package — the official/CommunityToolkit
Aspire catalog has no mock-server integration), registered in the AppHost **gated
on the `Parameters:UseHeappeMock` flag** (see `Program.cs`) — it is still
`ExcludeFromManifest`, and under `aspire run` it listens on a fixed port (9099) so
tooling can reach it at a known address.

Redirect HEAppE to it **without any API code change**: seed `HEAPPE_URL` with a
`__HEAPPE_MOCK_URL__` placeholder, substituted at runtime with the WireMock resource
endpoint (same mechanism as the idpid placeholders). WireMock mappings return
deterministic responses matching the seeded cluster/resource identifiers.

Waldur and Vault are **not** faked at the wire — no v2 endpoint calls them in the
request path.

## Consequences

- **+** Zero production/API code change; redirect is pure test data.
- **+** No hand-rolled stub project to own; canned mappings as JSON or via
  `IWireMockAdminApi`.
- **+** Reuses the existing placeholder-substitution and `*Extensions.cs` AppHost
  patterns.
- **−** WireMock mappings must be kept consistent with the seeded identifiers and
  the HEAppE response contract; drift shows up as a failing catalog assertion.
- **−** A new test-only dependency (`WireMock.Net.Aspire`) in the AppHost, gated on
  `Parameters:UseHeappeMock` so it never ships in a manifest.

ADR-0069 later moved the E2E suite to an in-process WireMock that reads this same
`heappe-mappings/` folder, alongside the AppHost resource.

## Alternatives rejected

- **In-process `IHeappeService` fake** — impossible in this harness (app is
  out-of-process).
- **Hand-written ASP.NET stub project as a ProjectResource** — more code and a new
  project to maintain for 2 endpoints.
- **Prism/Mountebank (OpenAPI mock)** — adds a non-.NET dependency and a spec to
  keep in sync.
