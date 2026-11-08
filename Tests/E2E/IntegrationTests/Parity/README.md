# ParityTests — scope

Live, side-by-side v1↔v2 **read** parity for the F7 cutover (issue #238).
Each spec calls the legacy v1 route and its v2 equivalent through the Gateway,
under real SQL, and asserts HTTP-status + JSON-shape parity (modulo declared
divergences). See [ADR-0055](../../../docs/architecture/adr/0055-f7-v1-v2-parity-suite.md)
and [v1-retirement-parity.md](../../../docs/ddd-migration/v1-retirement-parity.md).

## Out of scope

This suite is **reads-first by design** (ADR-0055, decision D3). The following are
deliberately **not** covered here — this list exists so a future reader never mistakes
silence for oversight. They are follow-ups, not gaps.

- **All writes** (`POST` / `PUT` / `DELETE`, assign / unassign) — deferred to **Phase 2**.
  Write parity needs a per-test seed reset (`GatewayAttachFixture.ResetToSeedAsync`)
  because writes are side-effectful; the reads suite intentionally avoids that cost.
  Write-only divergences (ProjectTag 409-on-in-use, RoleId route→body) are encoded then.

- **DDI / Service / DataStaging reads** (`SyncDetails`, `ProjectsInfo`, `WorkerData`,
  `DataStagingLocations`) — these run on the **service-to-service API-key** auth path,
  a separate auth dimension from the user-bearer flow this suite exercises. Explicitly
  noted, not silently dropped; a follow-up parity dimension per ADR-0055 Follow-ups.

- **`CyclopsUsage`** — Gateway-served, with **no real v2 endpoint**. Drop-by-design (F2);
  there is nothing to compare against, so it is intentionally excluded.

Everything else in the GET/list read surface (Project, Provider, Location, Requests,
ProjectResource, ProjectTag, User/UserInfo, Public, Administration, Enums/static/version)
is in scope for this suite.
