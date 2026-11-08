# ADR 0048 - Project-scoped, intent-specific onboarding endpoints (additive over the generic requests surface)

- Date: 2026-06-30
- Status: accepted
- Relates: ADR-0009, ADR-0018 (read-scope / anti-leak 404), ADR-0020 (v2 HTTP contract), ADR-0024 (zero-surrogate addressing), ADR-0046 (the aggregate), ADR-0047 (the table)
- Issue: [work item #228](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/228)

## Decision

The new `OnboardingRequest` aggregate is exposed through **intent-specific, project-scoped**
endpoints under the `onboarding-requests` segment, **added alongside** the existing generic v2
`requests` endpoints (which remain during the transition):

```
POST projects/{projectShortName}/onboarding-requests                 # any authenticated caller, NO scope check
PUT  projects/{projectShortName}/onboarding-requests/{slug}/accept   # RequireProjectScope(projectShortName)
PUT  projects/{projectShortName}/onboarding-requests/{slug}/reject   # RequireProjectScope(projectShortName)
GET  projects/{projectShortName}/onboarding-requests                 # received list, RequireProjectScope(projectShortName)
GET  users/me/onboarding-requests                                    # caller's sent list, cross-project, caller-scoped
```

> Superseded in part by [ADR-0107](0107-caller-centric-onboarding-request-list.md) (2026-09-24): both GET
> lists above and their rules below (the received list under "Accept / reject / received-list", and "Sent
> list"). `Users/Me/OnboardingRequests` now lists sent and received requests with a `Direction` filter, and
> the per-project GET is removed. Everything else here stands.

Segment name: **`onboarding-requests`** (decided over `membership-requests` / `join-requests`).

Rules:

- **Create** takes the project from the route; any authenticated caller may ask to join (no scope
  check — you do not yet have a role on the project you are joining). Keeps both legacy guards as
  `409`: already-an-active-member, duplicate-pending-request.
- **Accept / reject / received-list** enforce `caller.RequireProjectScope(projectShortName)` on the
  **route** project. Because both the project and the slug are in the path, the handler MUST also
  verify the resolved request actually belongs to the route project; on mismatch it returns the
  **same** `not_found` ProblemDetails as a genuine miss (ADR-0018 existence-hiding rider). This
  closes the cross-project oracle a `/projects/A/.../{slugOfB}` request would otherwise open.
- **Sent list** lives at `Users/Me/OnboardingRequests` — it is inherently cross-project (a user's
  own outgoing requests), so it cannot be project-scoped. It mirrors the existing `users/me/...`
  convention (`Users/Me/Preferences`) and returns only the caller's own requests.
- **No action links** in the new surface. Onboarding accept/reject is performed by an authenticated
  Project Owner/Manager from the UI. The existing action-link endpoints (`HandleActionLink`,
  `GetPublicRequestInfo`) and create-time token minting stay **unchanged on the old `Request`
  flow** and are not reproduced for `OnboardingRequest`.
- **Transition is additive.** The generic v2 `requests` endpoints (`POST Requests`,
  `PUT requests/{slug}/accept|reject`, `GET Requests`, `GET Requests/Sent`) are **kept**, marked
  deprecated in their OpenAPI description; no redirect shims. Their removal is a follow-up after
  the UI cuts over.
- Notifications reuse the existing `INotificationSender` (same templates, `SaveChanges`-then-notify,
  best-effort). Response/error shapes follow ADR-0020; addressing is slug-based per ADR-0024.

## Context

- The originating issue proposed dissolving the generic `requests` surface into intent-specific
  project-scoped endpoints. Planning kept the *shape* but made the rollout **additive** (keep old,
  add new) rather than a hard cutover, because the single UI consumer migrates wholesale and an
  additive window is lower-risk.
- ADR-0009 already moved accept/reject's project-scope check into Core (the legacy routes were
  request-id-only). With the project now in the route, the scope check reads directly off the path
  (`RequireProjectScope`), but the request-belongs-to-project assertion is newly required to keep
  ADR-0018's anti-leak guarantee.
- Read scoping stays at the **caller tier** (ADR-0018's 2026-06-16 amendment): no admin/provider
  tier branching on these reads.

## Considered options

- **Additive project-scoped endpoints, sent-list at `users/me`, no action links (chosen).** Matches
  the consumer's confirmed choices; lowest-risk transition; cleanest new surface.
- **Hard cutover (delete generic `requests` now).** Matches the issue's original headline but risky
  mid-transition; rejected in favour of additive.
- **Sent list under `userinfo/...`.** Rejected: `userinfo` is a denormalized profile read-model; a
  paged collection of the caller's own requests fits the `users/me/...` sub-resource convention.
- **Reproduce action links for `OnboardingRequest`.** Rejected: not reachable for onboarding in v2
  (tokens never emailed; PM handles from UI); would carry dead machinery into the clean aggregate.

## Consequences

- During the transition the OpenAPI documents describe both the deprecated generic `requests`
  endpoints and the new `onboarding-requests` endpoints; the tracked `API_*.json` snapshots
  regenerate **additively** (new paths added, old ones annotated deprecated). Diff is reviewed.
- The request-belongs-to-project check is a code-review checklist item for accept/reject (same
  `not_found` code for missing, out-of-scope, and wrong-project).
- Follow-up issue removes the generic `requests` self-onboarding endpoints post-cutover.
