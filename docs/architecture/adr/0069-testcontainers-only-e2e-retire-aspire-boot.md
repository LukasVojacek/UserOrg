# ADR-0069 — Testcontainers-only E2E: retire the Aspire boot branch entirely

- Status: Accepted
- Date: 2026-07-19
- Context: T260719-endpointcov (endpoint auth-matrix + validation coverage); builds on T260719-testcontainers
- Supersedes: [ADR-0055](0055-f7-v1-v2-parity-suite.md) §D2 (the two-Aspire-boots-in-CI cost)

## Context

ADR-0055 D2 accepted, as the cost of splitting parity tests into their own project, that
**each E2E test assembly boots its own AppHost** — `EndpointsTests` and `ParityTests` each
ran their own xUnit collection and therefore their own Aspire+SQL boot in CI. D2 flagged the
fallback explicitly: "if CI wall-clock becomes a problem the fallback is to merge the two into
one assembly; the shared fixture makes that reversible."

T260719-testcontainers landed that fallback and went further: `Tests/E2E/IntegrationTests`
now runs on `TestcontainersTopologyFixture` — a SQL container + WireMock (HEAppE/Vault/
EmailSender) + the API and Gateway launched as child processes, no Aspire anywhere in the
loop. `AspireGatewayFixture` still exists but only its **attach-mode** branch is reachable
from `IntegrationTests`; the `DistributedApplicationTestingBuilder` boot branch is now dead
code, reachable only from the standalone `EndpointsTests`/`ParityTests`/
`AppHostConfigurationTests` projects.

T260719-endpointcov (this task) builds the full v2 auth-matrix and validation coverage on
`IntegrationTests` and, once that coverage lands, retires the Aspire-booting projects
outright: `EndpointsTests` and `ParityTests` are deleted (their surviving specs migrated into
`IntegrationTests`), `AppHostConfigurationTests` is deleted, and `AspireGatewayFixture` is
renamed and stripped of the boot branch and the `Aspire.Hosting.Testing` package reference.
Zero test code references Aspire afterward.

## Decision

Retire the per-assembly-AppHost-boot mechanism from ADR-0055 D2 entirely, in favor of the
single `TestcontainersTopologyFixture` already running `IntegrationTests`:

1. `EndpointsTests`, `ParityTests`, and `AppHostConfigurationTests` are deleted. The coverage
   worth keeping — host-composition/CORS characterization, the parity specs, the v1/v2
   role/read/write matrix — moves into `IntegrationTests`, either as a direct port or as
   subsumption by the new auth-matrix and validation suites built in this task.
2. `AspireGatewayFixture` is renamed and stripped down to its attach-mode client-factory
   logic only; the `DistributedApplicationTestingBuilder` boot branch and the
   `Aspire.Hosting.Testing` package reference are removed.
3. The AppHost itself (`src/UserOrg.AppHost`) is **unchanged** and remains how `aspire run`
   starts the local stack — this decision retires only the AppHost's dedicated *test* boot
   path, not the AppHost.

## Consequences

- One E2E topology, one boot, in CI — the two-Aspire-boots cost ADR-0055 D2 accepted is gone.
- **AppHost-composition regression coverage is dropped with no direct replacement.**
  `AppHostConfigurationTests` was a model-inspection unit test over the AppHost's resource
  graph (which resources it wires, not whether they boot correctly) — a different risk class
  from the boot-branch removal in point 2. No equivalent assertion exists post-retirement; a
  broken AppHost resource wiring is now caught only by running `aspire run` manually or by
  CI/deployment surfacing it downstream, not by a dedicated unit test. This is a conscious
  gap, not an oversight — accepted because the AppHost changes rarely and the test net's
  maintenance cost (a second boot path to keep working) outweighed its catch rate.
- `Tests/E2E/ParityTests` and its declarative parity-spec table (ADR-0055 D1/D3/D4/D5) are
  unaffected in substance — they keep running as before, just physically inside
  `IntegrationTests` instead of a sibling project.

## Related

ADR-0055 (superseded §D2), ADR-0051 (WireMock external-service faking, reused by
`TestcontainersTopologyFixture`), ADR-0052 (deterministic seed/reset, reused).
