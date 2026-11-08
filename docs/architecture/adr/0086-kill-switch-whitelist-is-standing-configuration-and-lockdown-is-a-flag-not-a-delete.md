# Kill-switch whitelist is standing configuration; lockdown ignores it via a flag, never deletes it

Status: accepted — revises ADR-0077 §7 and ADR-0082 §2a (the whitelist parts only; the rest of both stands)

## Context

ADR-0077 treated the whitelist as a *parameter of the freeze*: it arrived inline on
`PUT /KillSwitch/{Provider}/{Location}`, and the lift deleted the rows (§7). A provider-wide
enforce deleted every whitelist row across the provider (ADR-0082 §2a) — that was the only way
to express "block everything" when the whitelist could only be supplied, never suppressed.

That shape forces the administrator to compose the list of still-allowed command templates
**during** the incident, from memory, under time pressure — the worst possible moment to decide
which templates a project cannot live without. It also means the same list must be rebuilt from
scratch after every lift.

The kill-switch is not deployed yet (#254, commit `a7292a16`), so the contract can still change
without a compatibility story.

## Decision

**The whitelist is standing per-Location configuration, prepared ahead of any incident. It
survives lift and provider-wide enforce. The kill-switch only ever reads it. "Block everything"
is expressed by a flag that ignores the whitelist for the duration of the freeze — never by
deleting it.**

1. **Whitelist is written on its own endpoint**, independent of the kill-switch's state:
   `PUT /api/Heappe/KillSwitch/{ProviderName}/{LocationName}/Whitelist`, body
   `SetLocationWhitelistRequest(IReadOnlyList<KillSwitchWhitelistItem> Items)`, **replace**
   semantics (an empty list clears it), returning `LocationKillSwitchDetail` so the caller
   re-renders from one response. Replace, not add/remove: the UI already holds the full list from
   the GET and posts what is ticked, so replace matches what the administrator sees and cannot
   drift. Whitelist rows carry `AddedByUserId`/`AddedAtUtc` — approving an exception mid-incident
   is as security-relevant as lifting the switch for one template, and must leave a trace.

2. **`Whitelist` is removed from `SetLocationKillSwitchRequest`** outright — no deprecation
   period. Two ways to write the whitelist would be exactly the dual source of truth this ADR
   exists to remove, and there is no deployed consumer to break.

3. **Lockdown is `IgnoreWhitelist`** (default `false`) on both the location and the provider
   request, persisted on `LocationCommandTemplateKillSwitch`. The kill rule — expressed once, in
   `KillSwitchStateService` — becomes:

   > killed = Location enforced AND (`IgnoreWhitelist` OR the `(location, template)` tuple is not whitelisted)

   Because the rule lives in one place, all four enforcement sites (HEAppE permissions, GetAll,
   `TemplatesInfo`, the kill-switch's own GET) inherit lockdown for free. `IgnoreWhitelist=true`
   with `IsEnforced=false` is a `400` — there is nothing to ignore when nothing is frozen — and a
   lift always writes `IgnoreWhitelist=false`, so a stale lockdown can never revive on the next
   enforce.

4. **The enforce response says what stayed allowed.** `KillSwitchLocationUpdateResult` gains
   `StillAllowed` — the whitelist as it stands *after* the request, empty under lockdown and on
   lift. `KillSwitchProviderUpdateResult` gains only `StillAllowedCount`, counted over the
   already-authorized location set: the provider path is a deliberate fast fan-out (ADR-0082) and
   must not pay for name resolution across every Location to tell the administrator something one
   GET away.

5. **No new listing endpoint.** `GET /KillSwitch/{Provider}/{Location}` already returns every
   template on the Location with `StoredState`, `EffectiveState` and `Whitelisted`, has no
   not-found path, and answers identically when the Location was never frozen — it *is* the list
   the UI ticks against. `Whitelisted` keeps meaning "is on the whitelist" (configuration the
   administrator edits, visible even under lockdown); "passes right now" is `EffectiveState`.
   `IgnoreWhitelist` is surfaced on both admin GETs so "ticked, yet Frozen" is explicable.

## Consequences

- **Provider isolation stays structural, not role-aware.** A Location has exactly one owning
  provider, every whitelist query is Location-scoped, and a foreign provider is refused `403`
  before the Location name is resolved. So a Platform Administrator may prepare whitelists across
  providers while a Provider Administrator enforcing his own Location reads only his own rows —
  not because the whitelist is filtered by role, but because he never reaches the others. The one
  place this can be broken is `StillAllowedCount`: written as a global `COUNT(*)` it would leak
  the number of other providers' exceptions. It is pinned by a test.
- **A whitelist row can outlive its template, and we let it.** No FK (module convention), no
  cleanup job, no hook into template deletion. An orphaned row matches no partition, so it is
  inert — it can never allow something that does not exist — and the next replace-PUT sweeps it.
  **Accepted risk:** a template's identity is its name, so *renaming* it silently drops it from
  the whitelist; the administrator finds out by its absence from `StillAllowed` after the next
  enforce. Judged acceptable against coupling the template lifecycle to the kill-switch for rows
  that do nothing.
- **The whitelist grants running, not changing.** It does not punch through the write guard
  (Activate/Deactivate still `409` on a frozen Location) and does not un-skip synchronization.
  Both of those write stored state — and synchronization also applies allocation expiry
  (ADR-0083) — which is precisely what must not happen mid-incident. A whitelisted template keeps
  running on the definition it had when the freeze began.
- **ProjectOwners and HEAppE learn nothing new.** `CommandTemplateResponse.Frozen` and HEAppE's
  `Enabled` are untouched; lockdown reaches them through the shared kill rule. `IgnoreWhitelist`
  stays on the admin endpoints, in the same class as `Reason` (ADR-0077).
- **Editing the whitelist must evict the cache unconditionally** — including while the Location is
  *not* enforced. Conditioning the eviction on `IsEnforced` saves one eviction on a rare operation
  and buys a race in which a freeze starting between the check and the write leaves a removed
  template launchable for the full 5-minute TTL (ADR-0084).
