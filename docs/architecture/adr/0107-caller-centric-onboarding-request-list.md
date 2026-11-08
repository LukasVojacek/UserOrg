# ADR 0107 - One caller-centric onboarding request list with a direction filter

- Date: 2026-09-24
- Status: accepted
- Supersedes in part: [ADR-0048](0048-project-scoped-onboarding-endpoints.md), its two list endpoints
- Relates: ADR-0006 (caller claims), ADR-0018 (read scope), ADR-0020 (v2 HTTP contract), ADR-0046 (the aggregate)

## Context

ADR-0048 gave onboarding requests two lists: `GET Users/Me/OnboardingRequests`, the requests the caller
sent across all projects, and `GET Projects/{projectShortName}/OnboardingRequests`, the requests received
on one project. An Owner or Manager of several projects needed one call per project to find the requests
waiting for their accept or reject. The deprecated `GET Requests` does not help, because it reads only the
old `Request` table.

## Decision

`GET api/v2/Users/Me/OnboardingRequests` is the one onboarding list, read from the caller's point of view.

- **Direction.** `Direction=Sent` returns the requests the caller created. `Direction=Received` returns the
  requests on projects where the caller is Owner or Manager. Without `Direction`, both are returned. The value
  is case-sensitive and must be exactly `Sent` or `Received`. Anything else, including `sent` and numeric
  strings, is `400 onboarding_request.invalid_direction`.
- **Received scope.** Only projects where `caller.ProjectRoles` holds the Owner or Manager role count, with the
  role names taken from `WellKnownRoles` and matched ignoring case. A Member role never contributes, and Admin
  gets no cross-project view.
- **Filters.** `State` filters by state name and defaults to all states. `ProjectShortName` narrows both halves
  to one project. A project outside the caller's reach yields only the caller's own sent requests there, or an
  empty page, never 404. A malformed `ProjectShortName` is `400 validation.invalid_format`.
- **Items.** Every item carries `Direction`. A request the caller sent to a project they now manage matches
  both halves and appears once, as `Received`, because the caller can act on it. Under `Direction=Sent` the
  received half is dropped, so the same request is listed as `Sent`.
- **Removal.** `GET Projects/{projectShortName}/OnboardingRequests` is removed, with its RBAC seed row.
  `Direction=Received&ProjectShortName=X` replaces it. POST, Accept and Reject stay under the project.
- **Gateway permission.** The path keeps `perm:user:request`, because every user may list their own sent
  requests. The received half is guarded in the handler by the scope above.

## Considered options

- **One path with a `Direction` filter (chosen).** One route, one RBAC entry, one set of tests.
- **`Sent` and `Received` sub-paths.** Two routes and two RBAC entries for the same read. The received half
  still needs the same in-handler scope, so the split buys no security.
- **A root `GET OnboardingRequests`.** Rejected in favour of keeping the caller-centric `Users/Me` path that
  ADR-0048 already serves.

## Consequences

- Develop is unreleased, so no alias for the removed GET is kept.
- Until the develop RBAC store is reseeded, a GET on the removed path still reaches RBAC, because the
  gateway's route-existence check (ADR-0106) decides on the path alone and POST keeps that path served. Owner
  and Manager then get 405 from Core and everyone else 403. No data is exposed.
- Who may accept or reject is unchanged: that check still reads `caller.Scope`.
