# ADR 0001 - Gateway: internal short‑lived JWTs and Service‑to‑Service (S2S) tokens

- Date: 2026-02-02
- Status: accepted

## Decision

The Gateway will generate short‑lived internal JWTs for forwarding authenticated client requests to backend services and will also generate service‑to‑service (S2S) tokens for validated callers. The gateway preserves the original client Authorization header in `X-Original-Authorization` and performs gateway‑side endpoint permission checks before issuing internal tokens.

## Context

- Backend services require an authorization token they can validate and use for access control.
- The team prefers centralized authorization enforcement and minimal runtime dependency of backends on the external Identity Provider (IdP).
- The system must support both user requests and service‑to‑service calls where a caller identity must be verifiable.

## Options considered

1. Forward client's original token to backends (no gateway token).
   - Pros: Simple; no token generation overhead on gateway.
   - Cons: Backends must integrate with external IdP; no centralized gateway‑level policy enforcement; larger attack surface for client tokens.

2. Use OAuth2 Token Exchange (RFC 8693) against the IdP.
   - Pros: Standard approach; IdP issues a token tailored for backend audience.
   - Cons: Requires IdP support and additional per‑request calls to IdP; added latency and operational dependency.

3. Gateway issues short‑lived internal JWTs (chosen).
   - Pros: Centralized policy enforcement in gateway, low latency token creation, backends trust gateway tokens without calling IdP, gateway can shape token claims (e.g. project list).
   - Cons: Gateway must securely manage signing keys and rotation; backends must trust gateway issuer.

4. mTLS between gateway and backends.
   - Pros: Strong mutual auth, less token management.
   - Cons: Complex certificate management, harder for local/dev environments.

## Rationale

Option 3 is implemented in the codebase (see references below). The team favored centralization, minimal runtime coupling of backends to the IdP, and the ability to easily embed custom claims into tokens (for example user project assignments). Generating short‑lived tokens in the gateway is a pragmatic, low‑latency solution that fits the .NET stack used by the project.

## Consequences

- Backends must validate and trust gateway‑issued tokens and be configured with gateway public keys or trusted issuer configuration.
- A key management and rotation strategy is required for gateway signing keys; auditing of token generation should be implemented.
- The gateway becomes a critical security boundary; its availability and security posture must be monitored.
- Consider caching user permission lookups to limit load on the authoritative role service.

## Code references

- src/Modules/Services/Gateway/UserOrg.Gateway/Program.cs — functions `AddInternalTokenTransform`, `AddInternalServiceToServiceTokenTransform`, and `AuthorizeRequestAsync`.

## Review / Notes

- Verify that signing keys used by the gateway are stored in Vault and that rotation is exercised periodically.
- Add telemetry/audit events for token generation and failed authorization checks.
