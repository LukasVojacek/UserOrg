# Allocation expiry is outside the kill-switch guarantee and must NOT be guarded

Amends the framing of [ADR-0077](0077-location-kill-switch-is-a-read-time-lens-at-partition-grain.md)
§5 and its "stored state never touched" consequence. ADR-0077's decision stands
unchanged; this ADR narrows what that promise actually claims, and records why one
apparent violation must be left alone.

## Context

ADR-0077 promises the kill-switch never overwrites
`ProjectCommandTemplatePartition.State`, and ADR-0077 §5 blocks manual
Activate/Deactivate/Create on a frozen Location (409) so a mid-incident state change
cannot persist past the lift.

The independent review of block B08 flagged an apparent contradiction:
`ProcessAllocationExpiration` (`ProjectCommandTemplateSyncService.cs:285-317`) runs an
`ExecuteUpdate` setting `State = Inactive` for **every** partition of a
`(ProjectId, ProjectResourceId)` — including partitions on frozen Locations — and it
runs at `:76`, **before** B08's frozen-location skip at `:94`. An automated deactivation
that bypasses the B07 write guard.

Investigation established:

- **Expiry's inputs are Core dates + the clock**, nothing else: `projectAllocation.StartDate/EndDate`
  and `Resource.StartDate/EndDate` vs `DateTime.UtcNow` (`:288-289`), sourced from Core via
  `GetProjectResourceAllocationWithLocationsAsync`. The kill-switch touches none of them.
- **Expiry is deactivate-only and latching**: no re-activation path exists. Once expiry flips a
  partition Inactive, a later un-expiry sync does NOT restore it — `DetermineTemplateState`
  (`ProjectCommandTemplateCreateService.cs:554-561`) only yields `Active` for a partition whose
  stored state is already `Active`.
- **Expiry's grain is (project, resource), never per-location** — so it deactivates partitions at
  ALL of a resource's locations, not just a frozen one.
- **The read path surfaces expiry independently of stored State**: `CheckAllocationExpiration`
  (`CommandTemplateManagementService.cs:187-196`) recomputes `isExpired` from the same four Core
  dates and ORs an `Expired` flag onto the response — it never relies on the stored flip.
- The kill-switch read overlay is confirmed pure projection (`ProjectCommandTemplate.cs:56-58`,
  in-memory only) — ADR-0077's promise holds for the kill-switch itself.

## Decision

**Allocation expiry is OUT OF SCOPE of the kill-switch guarantee. It must continue to run
before the frozen-location skip, and must NOT be guarded or deferred during a freeze.**

ADR-0077's promise is precisely: *the kill-switch does not mutate stored `State`*. It is **not**
*"no stored-state change may occur while a location is frozen"*. Expiry is an independent
lifecycle event whose outcome the freeze cannot influence.

The convergence argument is the whole reason:

- **As-is (expiry runs):** expiry reads only Core dates + the clock, so a frozen run and an
  unfrozen run produce **identical** stored state. The freeze changes nothing. The resulting
  `Inactive` is caused by expiry, and after the lift it is *correct* — the allocation genuinely
  expired.
- **If we guarded/deferred expiry during a freeze:** because expiry is a **latch**, deferring it
  would suppress a permanent state change that would otherwise have landed. The frozen run would
  then end in a **different** stored state than the unfrozen run — introducing exactly the
  divergence ADR-0077 exists to prevent. **The guard would cause the bug it was meant to stop.**

Therefore the ordering in `SyncCommandTemplatesForResource` — `ProcessAllocationExpiration` at
`:76`, frozen-location skip at `:94` — is **load-bearing**, not incidental. Do not "fix" it by
moving the skip earlier or by adding a kill-switch check to the expiry path.

## Consequences

- **No code change.** B08's ordering stands and is now documented as deliberate.
- **A future reader cannot mistake the guarantee's scope.** The B07 write guard exists because a
  *human* deactivating mid-incident is a discretionary change they may make *because of* the
  incident, losing their pre-incident setting. Expiry is deterministic and would have happened
  anyway — the two are not the same class of write, which is why one is guarded and one is not.
- **The convergence argument is not just reasoning — it was measured.** Two live lifts, same code,
  opposite outcomes, and the difference was expiry alone:
  - lifting a location whose allocation had **expired** deactivated ~44 partitions;
  - lifting a location whose allocation was **current** restored the permissions gate
    **byte-for-byte** to its pre-freeze response (72 rows, identical tuple set).

  So the freeze contributes nothing to the stored-state change; expiry does all of it. That is
  exactly what this ADR claims, now with a control.
- **The lift makes this visible, and an operator will read it as the lift's doing.** Confirmed
  against a live database: lifting one location took it from 51 Active / 1 Inactive to
  45 Inactive / 7 Active. §7's lift enqueues a resync, the resync runs expiry, and the
  allocation had already expired — so ~44 templates were deactivated *at the moment of the
  lift*. The reasoning above still holds (the background sync would have applied exactly the
  same expiry within its normal interval; the lift only pulled it forward, and the stored state
  a frozen run reaches is identical to an unfrozen one). But the operator sees "I lifted the
  kill-switch and it deactivated 44 templates," and will report it as a kill-switch bug. Expect
  that, and do not respond by guarding expiry — the argument above explains why that would be
  strictly worse. If the noise proves unacceptable, the honest fix is elsewhere: expiry's own
  semantics, or reporting the resync's outcome to the caller, not a freeze-aware expiry path.
- **Known, accepted asymmetries** (all pre-existing, none introduced by the kill-switch):
  - Expiry's blast radius is (project, resource)-wide, so an expiry firing during a
    single-location freeze deactivates partitions at that resource's *other* locations too.
  - Expiry aborts the sync unconditionally (`:316` returns `Failure` even when its write
    succeeded), so an expired allocation logs a failed sync roughly every 30 minutes via the
    Waldur pull — during a freeze and outside one alike.
  - The expiry latch means an extended/renewed allocation does not auto-restore its templates.
- **The complete set of stored-`State` writers** (for future reference): expiry
  (`SyncService.cs:304`), sync reconciliation (`ProjectCommandTemplateCreateService.cs:505,508`),
  create-new (`:49`) and re-pend (`:65-67`), and manual Activate/Deactivate
  (`CommandTemplateManagementService.cs:156`). The kill-switch is absent from this list — which
  is the invariant ADR-0077 actually asserts.
