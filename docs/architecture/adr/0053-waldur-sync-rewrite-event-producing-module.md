# Rewrite the Waldur integration as an event-producing sync module (Waldur.Sync)

**Status:** accepted (grilling session 2026-07-06) · relates to ADR-0054…0061

## Context

The Waldur integration (Waldur.EFP, Waldur.Puhuri, WaldurPlatform.Base, `Userorg.Adapter/Waldur`)
periodically pulls customers, projects, marketplace resources and project members from two Waldur
instances (EFP and Puhuri — two use cases, one API shape) and upserts them into the UserOrg database.

The current implementation is a channel-based producer/consumer pipeline (3 background services,
`Channel<T>` hand-offs, a 1–32 worker pool, drain phases, a dead-letter queue) whose consumers
(`WaldurEfpExporter` 1280 LOC, `PuhuriExporter` 712 LOC, ~900 LOC of DbContext extension methods)
write **directly** through `IUserOrgDbContext`. There is no change detection — every 30-minute
iteration re-upserts everything — and the pull side cannot be separated from the UserOrg write side.

We want to (a) keep the functionality, (b) make the code simple enough for long-term maintenance,
(c) prepare a seam so the pull side can later move to its own container communicating over
RabbitMQ, and (d) get precise per-item failure attribution in logs.

## Decision

Replace the four projects with a **new implementation** (logic is ported, code is not):

1. **`Waldur.Contracts`** — integration event types and the single port
   `IWaldurEventPublisher`. No dependencies beyond primitives. This is the only assembly both
   sides share, and the only one both sides of a future RabbitMQ deployment will see.
2. **`Waldur.Sync`** — the whole pull module: Waldur API client, sequential sync worker,
   in-memory SHA change detection, diff→event emission. Depends only on `Waldur.Contracts`.
   This is the part that can later be lifted into its own container.
3. **`Userorg.Adapter/Waldur`** (consumer) — one handler per event type plus the in-process
   `IWaldurEventPublisher` implementation (direct dispatch to handlers). Depends on
   `Waldur.Contracts` and the Core domain.

Dependency rule: `Waldur.Sync → Waldur.Contracts ← Userorg.Adapter/Waldur`. The arrows never
cross; the hexagonal boundary being cut is the **event port**, not internal plumbing.

**Instances are configuration, not projects.** EFP and Puhuri are two configured instances of the
same module (`Waldur:Instances` array: `Name`, `Enabled`, `BaseUrl`, credentials, interval,
EFP-specific iRODS/staging maps…). Each enabled instance gets its own hosted worker and its own
SHA map (keys prefixed by instance name); zero enabled instances → the module does not register.
Adding a third Waldur deployment is a config change, not code. Events carry the `Instance` name.

**The passive STOMP listener (`StompQueueBackgroundService`) is not ported.** The architecture
keeps a seam for a future "Waldur pushes an event → targeted re-fetch" trigger (a second input
into the same fetch→diff→emit step), but it is added only when actually needed.

## Consequences

- The old projects (`WaldurPlatform.Base`, `Waldur.EFP`, `Waldur.Puhuri`, the exporter side of
  `Userorg.Adapter/Waldur`) and their tests are deleted in the same feature branch (ADR-0059).
- Future RabbitMQ migration = swap the in-process `IWaldurEventPublisher` for a publishing
  implementation and host the handlers behind a queue consumer; the contract assembly is already
  the only shared surface.
- The 75-LOC per-platform wrapper projects disappear; platform differences live in options and in
  event payload content only.
