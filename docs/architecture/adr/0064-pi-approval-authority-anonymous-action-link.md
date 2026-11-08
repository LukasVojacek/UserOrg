# 0064 — PI approval authority is an anonymous single-use action link (no login, no PI role)

Date: 2026-07-14
Status: Accepted — line 26 amended by [0087](0087-transacted-resource-accept-and-conditional-link-burn.md)
(the legacy `ProjectResourceAssignment` accept path only; this ADR's own WorkflowEngine gate flow
is unaffected)
Issue: #251 (makes #231 unnecessary for this flow)

## Context

The PI (person responsible for a resource, identified by the address the requester supplies on the request
itself — **amended by [0085](0085-resource-request-legacy-input-shape-draft-removed.md)**, DEC-1; the
ADR-0065 catalog-record source `ProjectResource.PrimaryInvestigatorEmail` is gone, and no `ProjectResource`
row need exist when the link is minted) must
approve/reject assignment requests. The PI need not be a project member and may not exist as a user in our DB
or IdP at all. Alternatives considered: durable "PI" role + email row-filter (#231) — requires role reconcile
lifecycle; verified JWT email claim match — requires a gateway email claim (`Caller.Email` does not exist) and
an IdP account. Legacy solved this with emailed action links; the UX was raw but the trust model fits.

## Decision

- On request creation (and resend) the flow generates a **cryptographically random single-use token**, stores
  only its **hash** + TTL on the projection row, and emails the PI a link to a UI page.
- The UI page calls anonymous public endpoints: `GET Public/ResourceRequestActions/{token}` (detail) and
  `POST Public/ResourceRequestActions/{token}` with `{decision, reason?}` — a thin wrapper over
  `IWorkflowClient.ResumeAsync<PiDecision>`.
- Unknown, used, and expired tokens all return the identical 404 problem (ADR-0018 anti-leak).
- Tokens are invalidated on use, resend, and cancel. No durable PI role, no gateway email claim.
  **Amended by [0087](0087-transacted-resource-accept-and-conditional-link-burn.md):** on the
  legacy `ProjectResourceAssignment` accept path (`ActionLinkService.Handle` →
  `ProjectRequestService.AssignResourceAndAcceptAsync`, a separate, older action-link mechanism
  from this ADR's own public-endpoint flow) that transacted the accept, "on use" is now
  "on **successful** use" — a rolled-back accept leaves the link reusable.
- Owner/Manager compensations for the gate, which as originally decided had no timer: **resend-approval**
  (new token, re-email) and
  **cancel** (gate resumed with a cancel signal → terminal `Cancelled`).
  **Amended by [0085](0085-resource-request-legacy-input-shape-draft-removed.md):** the gate does
  now auto-expire (DEC-7, 30 days default, terminal `Expired`), reusing the same cancel signal
  path with a flag; resend-approval and cancel are unchanged, and a resend also postpones the
  expiry (it bumps the same `UpdatedAt` the sweeper measures from).

## Consequences

- PI onboarding friction is zero (no account needed); the token is the sole authority — its entropy, TTL,
  hashing at rest, and single-use enforcement are the security boundary and must be tested.
- If a logged-in PI experience (personal request list) is wanted later, a JWT email claim can be added then;
  nothing in this design blocks it. #231 stays closed-unneeded for this flow.
