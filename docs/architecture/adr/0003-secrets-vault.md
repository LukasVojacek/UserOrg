# ADR 0003 - Secrets: HashiCorp Vault via proxy agent

- Date: 2026-02-02
- Status: accepted

## Decision

Use HashiCorp Vault as the centralized secrets management solution. Services access Vault through a local Vault proxy/agent (`vaultProxy`) which is configured in the AppHost development environment. The project includes a `VaultSeed` step to initialize development secrets.

## Context

- Secrets (DB credentials, signing keys, API keys) must be centrally managed, auditable, and support rotation.
- Local development should be reproducible and not require manual secrets injection.

## Options considered

1. Environment variables / static config files.
   - Pros: Simple and low operational overhead.
   - Cons: No rotation, audit trail, or fine‑grained access control.

2. Cloud provider secrets manager (AWS Secrets Manager, Azure Key Vault, GCP Secret Manager).
   - Pros: Managed service, integrated IAM and rotation features.
   - Cons: Ties deployments to a cloud provider; not ideal for on‑prem or diverse environments.

3. HashiCorp Vault (chosen).
   - Pros: Portable across environments, strong policy model, audit logs, secret engines; supports dev seed/init patterns.
   - Cons: Operational complexity: initialization, unseal, policy management, agent deployment.

## Rationale

The codebase defines a Vault container, a `vaultProxy` agent, and a `VaultSeed` Dockerfile to initialize development secrets (`src/UserOrg.AppHost/Program.cs`). Integration points exist in API and Gateway via `builder.AddHashiCorpVaultIntegration()`. Vault provides the desired features while supporting both local dev and production scenarios.

## Consequences

- Must secure Vault bootstrap tokens and production authentication backends (e.g., AppRole, OIDC).
- Add operational runbooks for key rotation, backup, and recovery.
- Consider caching secrets with TTLs in services to reduce latency and dependency on Vault availability.

## Code references

- `src/UserOrg.AppHost/Program.cs` — vault container and proxy definitions.
- `src/Core/API/Program.cs` and `src/Modules/Services/Gateway/...` — `AddHashiCorpVaultIntegration()` calls.

## Review / Notes

- Ensure production uses secure auth methods for Vault access (avoid root tokens in production).
- Document mount points and secret paths used by services (e.g., `VaultCredentials__MountPoint`).
