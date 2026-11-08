# ListComplete gate: partial list fetch suppresses the absence sweep

**Status:** accepted (grilling session 2026-07-17) · relates to ADR-0062, ADR-0055

## Context

Per-item isolation (ADR-0062) carries forward failed **detail** fetches by keeping the known item
id present in the diff input. That mechanism cannot cover a failed **list** page: when a page of
`GetCustomersAsync` or `GetProjectsAsync` fails, the ids of the items on that page are unknown, so
they cannot be carried forward. Omitting them would make `DetectAbsences` treat them as absent →
breadcrumb removal → spurious re-`Created` next iteration.

Naively taking only the successful pages is therefore unsafe. But aborting the whole iteration on
any list-page failure throws away progress on the items that *were* fetched.

## Decision

Add a **ListComplete gate** scoped to the customer and project list fetches:

- When any page of a list fetch fails, return the successfully fetched pages **plus a
  completeness signal** (`IsComplete = false`) instead of throwing. The present items are still
  upserted normally (upserts are per-item and idempotent via the SHA breadcrumb).
- `DetectAbsences` is **skipped for that entity type** while its list is incomplete, so no
  breadcrumb is dropped for an item that may merely be on the failed page.
- The signal is threaded: list fetch → `BuildProvidersAsync`/`BuildProjectsAsync` → a per-entity-
  type flag on the diff input → the `DetectAbsences` gate. The failed page is logged at Error
  (ADR-0066).

**Scope constraint (correctness):** the partial-return behavior is confined to the customer/project
**list** fetches. Detail fetches (offerings, resources, users) **keep throwing** so their map-lambda
`try/catch` marks them Failed (ADR-0062/ADR-0065). The paginating primitive must not be made
globally non-throwing — that would make a partial offering/resource list look complete.

## Consequences

- A transient list-page failure yields partial forward progress instead of a discarded iteration.
- Composes cleanly with resource-granularity skip (ADR-0063): an incomplete customer list means
  those customers' offerings are absent → their resources are skipped by the offering→customer
  rule, while the provider absence sweep is suppressed by the gate. Both mechanisms agree.
- "Never present partial as complete" (ADR-0056) is honored: absence detection — the only thing a
  partial list could corrupt — is turned off for the incomplete type; only safe upserts proceed.
