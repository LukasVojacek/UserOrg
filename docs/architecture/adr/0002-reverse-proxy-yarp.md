# ADR 0002 - Reverse proxy: In‑process YARP (Yet Another Reverse Proxy)

- Date: 2026-02-02
- Status: accepted

## Decision

The system uses YARP as an in‑process reverse proxy hosted inside the Gateway application. Request transforms (path rewriting, header enrichment, token substitution) are implemented using YARP transform builders and .NET request transforms.

## Context

- The gateway must perform dynamic logic on requests (permission checks, internal token generation) using DI services and application code.
- The team prefers to implement request transforms and token creation in .NET to reuse existing libraries and code.

## Options considered

1. External proxy (NGINX / Envoy) in front of microservices.
   - Pros: High performance, rich proxy ecosystem, independent scaling.
   - Cons: Harder to invoke application DI logic; more complex to wire dynamic token generation and permission checks.

2. Cloud provider LB/managed proxy.
   - Pros: Managed, scalable.
   - Cons: Platform lock‑in, limited in‑process transform capability tied to application logic.

3. YARP (in‑process) (chosen).
   - Pros: Native .NET transforms, direct access to DI, convenient for internal token generation and permission checks; simple deployment model using a single gateway service.
   - Cons: Gateway is a critical process requiring scaling; may need support from an external reverse proxy for advanced traffic management or DDoS protection.

## Rationale

The existing codebase wires YARP and uses C# transforms (`AddInternalTokenTransform`, `AddInternalServiceToServiceTokenTransform`, `MapForwarder(...)`). This approach aligns with the desire to perform authorization and token manipulation using DI and existing .NET services. YARP simplifies implementing complex runtime logic that would be cumbersome in an external proxy.

## Consequences

- The gateway process must be treated as a stateless, horizontally scalable component (scale instances and use a fronting load balancer in production if needed).
- Performance testing and tuning of YARP under expected load is required.
- If advanced proxy features are later required (WAF, DDoS), an external proxy may be introduced in front of YARP.

## Code references

- src/Modules/Services/Gateway/UserOrg.Gateway/Program.cs — `AddReverseProxy`, `MapForwarder`, YARP transform builders.

## Review / Notes

- Define gateway scaling guidelines and autoscaling rules for production.
- Consider a fronting proxy for global TLS termination, WAF, and rate‑limiting if needed.
