# ADR 0004 - Persistence: SQL Server (EF Core) + immudb for immutable audit

- Date: 2026-02-02
- Status: accepted

## Decision

Primary transactional and domain data will be stored in Microsoft SQL Server using EF Core. An append‑only immutable store (immudb) will be used for audit/immutability requirements and tamper‑evident logs.

## Context

- The application domain fits a relational model with transactions and constraints.
- Some business and security requirements demand a tamper‑evident audit trail.

## Options considered

1. Use a single relational database for both domain and audit.
   - Pros: Operational simplicity.
   - Cons: Harder to prove immutability and tamper proofing.

2. Move to event sourcing (event store) for primary model.
   - Pros: Native append‑only model, easier auditability.
   - Cons: Major architectural change and migration effort.

3. SQL Server (EF Core) for domain + immudb for immutable audit (chosen).
   - Pros: Keeps existing relational model, adds tamper‑evident store for audit; less disruptive to domain code.
   - Cons: Two different data stores to operate; need to coordinate writes (or accept eventual consistency for audits).

## Rationale

Project code configures EF Core with SQL Server (`AddDbContext<DatabaseContext>(... UseSqlServer(...))`) and AppHost includes immudb containers for development. This hybrid approach allows the team to retain the relational model while providing an immutable store for critical events, without committing to full event‑sourcing.

## Consequences

- Operations must manage both SQL Server and immudb (backups, monitoring, retention policies).
- Where strong consistency between domain state and audit is required, ensure write ordering or transactional coordination patterns are applied.

## Code references

- `src/Core/API/Program.cs` — EF Core configuration and `DatabaseSeedService`.
- `src/UserOrg.AppHost/Program.cs` — immudb container config.

## Review / Notes

- Define retention and backup policies for immudb and SQL Server.
- Implement instrumentation to verify audit write success in production.
