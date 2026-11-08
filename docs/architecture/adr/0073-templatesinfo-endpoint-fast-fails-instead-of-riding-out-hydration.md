# The TemplatesInfo endpoint fast-fails instead of riding out hydration

## Context

`GET /TemplatesInfo/{projectShortName}` sits behind a proxy with a **hard 60 s
limit**: at 60 s the proxy drops the connection and logs `499`. The endpoint must
therefore return within ~55 s, leaving margin for serialization and the proxy hop.

A TemplatesInfo build can take far longer than that today, for two compounding reasons:

1. **Application layer.** A build that queries several HEAppE instances is marked
   degraded if **any one** of them fails (`ProjectService.BuildProjectTemplateInfoAsync`).
   A degraded build enters a wait-and-rebuild loop bounded by `TemplatesInfoRetryPolicy`
   (default delay 10 s, budget 1 min). So a single dead instance forces the caller to
   wait out the whole budget — and then returns the same partial data that was already
   in hand at the first attempt.
2. **HTTP layer.** A dead instance's data calls run on the `HeappeClient`, which carries
   the standard resilience handler (per-attempt 45 s, total 120 s, **with retries**).
   `Task.WhenAll` over the instances cannot resolve until the dead instance's call
   exhausts that budget — up to 120 s, the retries re-attempting an unreachable host.

ADR 0072 deferred the underlying tension: raising `RetryBudget` toward ~2–2.5 min would
let a single request ride out the ~2 min hydration window, "at the cost of a longer wait
before a genuine outage returns 503 — left as a separate tuning decision." Under a 60 s
wall that decision can no longer be deferred, and the two callers of
`ProjectService.ProjectTemplateInfo` want opposite things:

- **The synchronous endpoint** must answer fast and cannot ride out a ~2 min window.
- **The background cluster warmer** (`ClusterInfoCacheWarmer`, fire-and-forget, no
  caller waiting) *wants* to ride it out — warming the cache is its entire purpose.

The existing `heappe/Health` probe is already the first remote call per instance, but it
only resolves the instance **version**: on any failure it falls back to the DB version
resolver and the data calls proceed anyway — which is what lets a dead instance reach the
120 s HTTP path.

## Decision

**Both callers do a single build attempt — no wait-and-rebuild loop.** The endpoint
fast-fails because of the 60 s wall; the background warmer drops the loop too because it
is only an optimization (it pre-warms the cache so the user's first real call is fast) and
already re-fires on later logins, so riding out a hydration window inside one run buys
little — especially as its budget (1 min) was shorter than the window (~2 min) anyway.
Concretely:

1. **The `heappe/Health` probe becomes a liveness gate.** It is probed first, on the
   dedicated health client (flat 15 s timeout, no retries). Success → resolve version and
   call the instance's data endpoints. Failure within the timeout → the instance is
   **skipped**: it contributes no data and marks the build degraded, but its data calls
   are never attempted. This **replaces** the DB-version fallback for an unreachable
   instance: a 15 s no-retry probe that cannot get a success means the data calls would
   not succeed either.

2. **Partial degradation returns immediately.** When some instances answered and at least
   one failed, `ProjectTemplateInfo` returns the available snapshot at once instead of
   entering the wait-and-rebuild loop. The "never cache a degraded build" invariant from
   ADR 0072 is preserved — the snapshot is returned out-of-band and nothing is cached.

3. **Total degradation 503s immediately.** When every instance fails its gate, the
   endpoint returns 503 without waiting — riding out hydration cannot help under the wall.

4. **Retries are disabled on the `HeappeClient` data calls**, as a backstop for an
   instance that passes the health gate but stalls mid-call: one attempt, no 120 s
   retry multiplication.

   This is configured on the shared `HeappeClient`, so retries are off for **every**
   operation routed through `HeappeService.SendRequestAsync` (cluster fetch, session
   codes, job submission, file transfer), not only TemplatesInfo. Accepted on purpose:
   the only path with a hard latency wall is TemplatesInfo, and disabling retries
   globally also removes the prior risk of **retrying non-idempotent writes** (e.g. a
   timed-out job submission that actually succeeded server-side being re-sent as a
   duplicate). If a specific *read* operation later proves to need retries, give that
   call its own retrying client rather than re-enabling them for the whole client.

5. **The wait-and-rebuild loop and `TemplatesInfoRetryPolicy` are removed.** A degraded
   build returns immediately (partial → snapshot, total → 503) and is never cached, for
   both callers. The warmer rides out hydration windows by **re-invocation** on the next
   login (30 s dedup marker), not by an inner loop.

## Consequences

- A project spanning a healthy and a dead HEAppE instance returns the healthy instance's
  data in roughly the time of the health probe (~15 s), not after the full retry budget.
  This is the behaviour the change is for.
- A **flaky-but-alive** instance — one whose `heappe/Health` blips within the 15 s window
  but whose version is known from the DB — is now **skipped** rather than served. This is
  a deliberate reversal of the DB-version fallback, accepted because the data calls would
  almost certainly fail too.
- Hydration is no longer ridden out within a single call by anyone. A user (or the warmer)
  hitting an instance mid-hydration gets a partial (or 503) response now; the cache heals
  on the **next** call/login once the instance finishes hydrating. This supersedes ADR
  0002's "the existing retry/snapshot machinery self-heals within the budget" — healing is
  now strictly across calls, not within one.
- The 503-on-total-degradation contract from ADR 0072 is kept, but now fires at ~15 s
  (gate timeout) rather than after the ~1 min retry budget.
- `TemplatesInfoRetryPolicy` and the wait-and-rebuild loop are deleted. There is no longer
  a retry-budget tuning knob to reason about on either path.
- **Per-instance worst case is ~60 s, not ~55 s — accepted.** The gate (15 s) and the data
  call (45 s) run *sequentially* per instance, so an instance that answers `heappe/Health`
  right at the 15 s edge and then stalls the data call for the full 45 s reaches 60 s — the
  proxy wall, with no margin. Accepted rather than tightened: 15 s is the *ceiling for
  declaring an instance dead*, not the typical probe time — a live instance answers health
  in well under a second, so the realistic worst case is ~45 s (data only) and a dead
  instance is ~15 s (gate only). The 60 s corner requires a pathologically slow-but-not-dead
  instance. If that corner is ever observed in practice, the cheapest fix is lowering
  `AttemptTimeoutSeconds` to ~40 s (→ 15 + 40 = 55 s) rather than adding an overall deadline.

> **Refined by ADR 0074.** The "total degradation → 503" contract here is narrowed to
> total degradation *from HEAppE-side causes*; a failure of an internal precondition (the
> active-command-template DB read) now fast-fails as **500** before the HEAppE fan-out, and
> the 503 body now carries a per-instance `failures[]` cause list. Partial → snapshot and
> "never cache a degraded build" are unchanged.
