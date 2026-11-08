# ADR 0017 - The DDI-facing service API (data-staging keys + provider sync/projects info) is a separate module, not part of Core

- Date: 2026-06-10
- Status: accepted

## Decision

The legacy service-to-service endpoints consumed by the **DDI** (Data & Distributed Infrastructure / data-staging) service migrate into a **dedicated module `src/Modules/Services/Ddi`**, not into the modular Core. The module covers:

- `GET /api/DataStaging/WorkerData`, `GET /api/DataStaging/DataStagingLocations`, `POST /api/DataStaging/DataStagingLocations` — read/bulk-write of DDI data-staging keys, backed by the HashiCorp Vault client.
- `GET /api/Service/SyncDetails` — per-provider synchronization info (resolved from the provider API key).
- `GET /api/Service/ProjectsInfo` — projects grouped by provider with their active resources.

The DDI module is a **facing / composition module**, not a domain module: it owns no aggregates. It composes the **Core read side** (`Core.DataAccess` query ports / read DTOs) and the **Vault client** (`HashiCorpVault.Contracts`) into a DDI-shaped contract, and is authenticated by the **provider API key** (S2S), not the user/portal token. It depends on `Core.DataAccess` **read-only** and never on Core's domain write side.

Core v2 keeps **only** the user/portal-facing business surface. `api/version` stays in Core (`GetVersion`); the rest of the legacy `/api/Service` + `/api/DataStaging` group moves to DDI.

## Context

- Goal of the Core refactor is **full v1 parity so legacy `src/Core` can be deleted** after a deprecation window (see [v1-retirement-parity.md](../../ddd-migration/v1-retirement-parity.md)). That makes every legacy route an obligation — but not every route belongs in Core.
- These three+two endpoints share a **single external consumer** (the DDI service) and a **single auth model** (provider API key via `httpContext.TryGetProviderId`, not a user token). The `SyncDetails`/`ProjectsInfo` handlers *read Core domain data* (`IAdministrationService.GetSyncDetails`, `IProjectResourceService.GetAllProjectResourceWithDetailsAsync`), which superficially argues "leave them in Core."
- A bounded context is defined by its **consumer and language**, not by which tables it reads. The DDI contract is a stable machine-to-machine surface with its own lifecycle and auth; mixing it into Core's portal-facing API couples two surfaces that evolve independently.
- The codebase already isolates cross-cutting surfaces this way: `Modules/Services/Gateway` (forwarding, incl. Cyclops), `Modules/Integrations/Heappe` (owns `api/Heappe/*`), `Userorg.Adapter` (bridges Core ports to Heappe). A DDI facing module is the same shape.

## Considered options

- **Dedicated `Modules/Services/Ddi` facing module (chosen).** Single consumer + single auth model = a real bounded context; keeps Core portal-only; the S2S contract evolves independently. Composes `Core.DataAccess` (read) + Vault. Cost: a new module that reads Core's read-side from outside Core (a deliberate, one-directional read dependency).
- **Keep them as a `/service` area inside Core.** Rejected: drags an API-key S2S surface and Vault dependency into the user-token business module, and blurs Core's responsibility. The fact that two handlers read Core data is not enough to make them Core's *contract*.
- **Fold DataStaging into the HashiCorpVault module and SyncDetails/ProjectsInfo into Core.** Rejected: splits one consumer's contract across two modules by data-source, fragmenting the DDI surface the consumer actually sees.

## Consequences

- **Core's surface stays coherent.** Core v2 is exactly the portal/user-token business API; the DDI S2S contract lives and versions on its own.
- **A one-directional read dependency `Ddi → Core.DataAccess` is acceptable and expected.** DDI reads through the read-side query ports/DTOs; it must never reach Core's domain write side. If a DDI read needs data no query exposes yet, add a read query in `Core.DataAccess` (same rule as any read).
- **Auth boundary is explicit.** DDI endpoints authenticate by provider API key; they do not share the user-token `Caller`/RBAC model of Core v2.
- **Parity, not behavior change.** The migration reproduces the existing DDI endpoints faithfully (including the Vault bulk-update); reshaping the DDI contract is out of scope.
- **Retirement bookkeeping.** These five routes are tracked as bucket **B2/B3** in the v1-retirement parity checklist; they must exist in the DDI module before v1 is cut off, but they are **not** part of the Core backlog.
