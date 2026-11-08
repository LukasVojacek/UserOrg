# waldur-acceptance

Offline harness for the Waldur cutover acceptance gate (ADR-0059). It answers two questions:

1. **Parallel-run parity (A1):** does the NEW `Waldur.Sync` path produce an equivalent UserOrg DB
   state to the OLD exporter for the same test-Waldur content?
2. **Non-empty-DB safety (A3):** on a production DB, do any rows carry a `ServiceName` that does not
   match their true instance (the buggy Puhuri path wrote `EFP-Waldur`)? Those rows would be missed
   by the corrected upsert keys and duplicated.

It is a plain args-in / console-report-out .NET tool. No Aspire, no DI, no config framework. It
holds **no product reference** (raw parameterized SQL only) so it survives the B14 cutover that
deletes the old Waldur code, and is reusable for the later RabbitMQ move.

## How the comparison avoids matching zero rows (ADR-0059/A1)

A naive dump-diff matches nothing: rows carry `Guid.CreateVersion7()` ids, `now()`-fallback
`CreatedAt`s and `SyncFrom`-written `ModifiedAt`s. So the tool:

- projects each entity to a **natural-key row** — the key is external ids / names
  (`externalId|serviceName`, resource `name|serviceName`, membership `projectExternalId|userName`,
  …) and FKs are joined through to those external ids, never surrogate GUIDs
  (`Sql/ProjectionSpecs.cs`);
- applies an **exclusion list** — surrogate ids, `CreatedAt`, `ModifiedAt`, audit users — so those
  never cause a diff (`Core/ProjectionDefaults.cs`);
- classifies every remaining delta against the pre-approved **expected-differences list**
  (`Core/ExpectedDifference.cs`), which encodes all five ADR-0059/A1 items:
  removed-project-now-lands-as-`IsDeleted=true`, per-resource partial progress,
  per-user-vs-all-or-nothing membership, manual-member sweep timing, eliminated no-op writes.

Old and new write the **same** UserOrg schema — they differ only in rows/values, not table shape —
so there is one projection, run against two connection strings, then diffed. Anything not equal and
not on the expected list is reported as **UNEXPLAINED** and must be explained or fixed before the
gate passes.

## Build

```bash
dotnet build Tools/waldur-acceptance/WaldurAcceptance/WaldurAcceptance.csproj
```

## Usage

```bash
# A1 parallel-run diff
waldur-acceptance diff \
  --old "Server=...;Database=userorg_old;..." \
  --new "Server=...;Database=userorg_new;..." \
  [--efp-core-service-name "EFP-Core"]

# A3 non-empty-DB ServiceName check — histogram by default
waldur-acceptance data-check \
  --db "Server=...;Database=userorg_prod;..." \
  --instance "Puhuri" \
  --true-service-name "Puhuri-Waldur" \
  [--owned-ids ./puhuri-external-ids.txt]
```

**`diff` outcome / exit codes (three-state):**

- `0` — **CLEAN**: every row equal.
- `1` — **PASS WITH DIFFERENCES**: only expected (ADR-0059/A1) differences, no unexplained. This is
  **not** a clean pass — the tool lists every expected-difference row (entity, key, matched rule) and
  a human must sign off on each before B14. A scripted gate that treats exit 1 as green is the
  caller's error; the tool never prints a bare "PASS" here.
- `2` — **FAIL**: one or more unexplained differences (incl. a `DUPLICATE natural key`), or a usage
  error. Explain or fix before cutover.

`--efp-core-service-name` names the EFP storage-reconciliation `ServiceName`; those
reconciliation-owned `AssignedLocation` rows are excluded from the projection (D14), matching the
payload-mapping rule so they cannot show up as spurious differences.

### A3 data-check — histogram, and why ServiceName alone is insufficient

By default `data-check` prints a **per-table ServiceName distribution histogram** (e.g.
`Project: EFP-Waldur=40, Puhuri-Waldur=3`). This is honest: the buggy Puhuri path wrote rows with the
**EFP** `ServiceName` *by definition*, so `ServiceName` alone cannot identify which rows belong to
Puhuri — the tool has no independent ownership signal. The histogram lets a human spot buggy
EFP-named Puhuri rows using their own knowledge. `--instance` labels the histogram; it does **not**
filter.

For the **sound** attributed count, supply `--owned-ids <path>` — a file of external ids (one per
line) known to belong to the instance under audit, obtained from the Waldur API pull for that
instance. The tool then counts, per table, the **owned** rows whose `ServiceName` is not
`--true-service-name` (exit 1 on hits, 0 when clean). Without `--owned-ids` the tool prints only the
histogram plus a note that attribution needs the owned-id set, and does not emit a CLEAN/HITS verdict.
The pure aggregation (`Core/ServiceNameMismatchCheck.cs`) is unit-tested; the SQL is validated at the
live run.

## Live parallel-run procedure (developer, session worktree)

> Run this in the **session worktree with Aspire**, never a block worktree — block worktrees share
> `bin`/`obj` output locations and Aspire would lock them.

1. Provision **two empty UserOrg databases** (`userorg_old`, `userorg_new`), same schema/migrations.
2. Point **both** runs at the **same test Waldur instance**.
3. **Stub or sandbox HEAppE** for both runs. Running both against the same live HEAppE would double
   external side effects (ADR-0059/A1). Use a stub client or a throwaway HEAppE sandbox.
4. Run the **OLD** integration against `userorg_old`, **one instance at a time** (EFP, then Puhuri).
   The old DI registers unkeyed singletons for `IExporter` / `Channel` / `ICollectorService` and
   never supported concurrent instances.
5. Run the **NEW** `Waldur.Sync` against `userorg_new` (instances may run concurrently).
6. Let each side complete at least one full iteration, then stop them.
7. Run `waldur-acceptance diff --old <userorg_old> --new <userorg_new>`.
8. The gate is fully green only on **CLEAN** (exit 0 — every row equal). On **PASS WITH DIFFERENCES**
   (exit 1) a human must sign off on each listed expected difference: an `OnlyInOld` membership can be
   a genuine NEW-side loss (a user silently loses access), and an `OnlyInNew` resource/membership can
   be a wrong-project resource or a role-map bug — confirm each is really the ADR-0059/A1 scenario.
   **FAIL** (exit 2, incl. any `DUPLICATE natural key`) is always a blocker.

Puhuri parity is validated by **intent, not today's code** (ADR-0059/A2): the old Puhuri path is
broken (writes EFP service names, user export can never succeed). Old-vs-new equivalence is only
meaningful for **EFP**; for Puhuri, assert the intended semantics (service name = instance name, own
service account, no storage reconciliation) rather than byte-equality with the old run.

## Restart-stability check (deferred W3 / I1 churn)

The NEW sync keeps its SHA breadcrumb map in memory and cold-rebuilds it from the DB on start
(D1/D13). A DB→DTO vs API→DTO drift would make the sync re-emit events forever. Measure it
empirically:

1. Run the NEW sync once against `userorg_new` to steady state; note the per-iteration event count
   from the `Waldur.Sync` OTel iteration counter (or the always-log iteration **summary** line).
2. Run it a **second time against the same DB** (restart → cold rebuild → one iteration).
3. **Assert the 2nd pass emits NO events** (`ProjectCreated/Updated`, `ProviderUpdated`,
   membership, `StorageConfigurationChanged` all zero, ignoring the one unconditional startup
   `StorageConfigurationChanged` for EFP-configured instances).

A non-zero 2nd-pass event count is the deferred W3/I1 churn (mapping/normalization nuances that make
the round-trip hash unstable): capture the emitting entity from the structured log and fix the
mapping/normalization, do not silence it.

## B09 removal/permission auth check

**Question (escalated from B09):** does the auth model grant a user project access via a
`UserPermission` whose Waldur assignment **and** access rows are BOTH soft-deleted?

**Verdict: YES — this is a real gap.**

- The effective authorization query (`GetUserPermissionsAsync` in
  `src/Modules/Libraries/Forge.Core/Features/UserOrgRoles/UserOrgDbContext.cs`) builds a user's
  roles/permissions **solely** from the `UserPermission` table:
  `from UserPermission up … where u.IdentityProviderUserId = @id and up.IsDeleted = 0 and
  (up.ProjectId is null or p.IsDeleted = 0)`. It does **not** join `AssignedUserToProject` or
  `UserProjectAccess`. This same source feeds both `/UserInfo` (`UserInfoEndpoint`) and the gateway
  permission cache (`UserPermissionsCache`).
- The Waldur removal handler
  (`src/Userorg.Adapter/Waldur/Sync/Handlers/UserRemovedHandler.cs`) performs a **dual** soft-delete
  of `AssignedUserToProject` **and** `UserProjectAccess`, but leaves the `UserPermission` row
  untouched (its `IsDeleted` stays `0`).

Because authorization reads only `UserPermission.IsDeleted`, a user removed from a Waldur project
**still has the effective project role/permission** after both the assignment and access rows are
soft-deleted. The dual soft-delete does not revoke access.

**Follow-up (do NOT implement in B13):** `UserRemovedHandler` should also soft-delete the matching
`UserPermission` row(s) for the `(project, userName)` pair (idempotent, same terminal
`SaveChangesAsync`), making it a *triple* soft-delete so the effective grant is actually revoked.
Small, self-contained change; pin it with a handler test that asserts `GetUserPermissionsAsync` no
longer returns the project role after removal.
