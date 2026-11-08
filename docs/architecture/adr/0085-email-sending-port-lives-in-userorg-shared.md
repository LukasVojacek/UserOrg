# 85. The `IEmailSender` port lives in `UserOrg.Shared`, not in `Core`

Date: 2026-07-25

## Status

Accepted

## Context

Issue #229 needs a single port that any host (Core, and eventually the legacy v1 stack) can
depend on to send a templated email, and a real transport that implements it. Two things had to
be settled before the adapter (#229 follow-up) could be written against a stable shape.

**Where does the port live?** `docs/ARCHITECTURE_GUIDE.md:239` and ADR-0079 establish the
default: a module defines its own ports under its `Ports/` folder, and `Core`'s public surface is
frozen to composition seams plus one documented exception,
`Features/Provider/ResourceAssignment/IResourceSyncRunner`. That exception is explicitly a
**grudging** one — ADR-0079 notes it "disappears only when the durable-outbox decoupling …
deletes it", i.e. it is debt the project intends to pay down, not a precedent to build on.
`IEmailSender` does not fit the same shape: it has no single owning module — the legacy v1 stack
is a documented future consumer (issue #229's own follow-up) — and putting it in `Core/Ports/`
would grow, not shrink, Core's frozen public surface that ADR-0079 just finished narrowing.
`UserOrg.Shared` is already the project every DbContext registration site depends on for the
cross-cutting `ConfigureSqlCommandLogging` extension (ADR-0076); it is reachable from Core and
from a future legacy-v1 caller alike, and it carries zero `ProjectReference`s of its own, so
`EmailMessage` is defined in plain `string`s rather than the `Core.Domain` `Email` value object.

**Who is allowed to own outbound HTTP?** Today all outbound HTTP integration lives in the
integration projects (`Heappe`, `Waldur`); `Userorg.Adapter` itself holds only `Heappe/` and
`Waldur/` *orchestration* adapters, no HTTP client of its own. The email-sending adapter (#229's
next block) is the first case of `Userorg.Adapter` owning an outbound HTTP call directly.

## Decision

1. `IEmailSender` and `EmailMessage` are defined in `src/UserOrg.Shared/Email/`, as a
   cross-cutting outbound port rather than a `Core`-owned one. `EmailMessage` carries only plain
   `string`s (`TemplateName`, `Receivers`, `TemplateArguments`, optional `Cc`, `Priority`) — no
   `Core.Domain` type crosses into `UserOrg.Shared`, keeping it at zero `ProjectReference`s.
2. This is a one-off exception, not a rule change: `Userorg.Adapter` owning the HTTP transport for
   this one port does not mean "adapters own HTTP now" — every other integration keeps its HTTP
   client where it already lives.
3. Cross-reference: ADR-0076 is being amended (sibling block of this task) to widen its stated
   reach and to cross-reference this ADR back; both land on the same feature branch.

## Consequences

- `Core`'s frozen public surface (ADR-0079) is untouched by this feature — no new port is added
  there, and the exception it already carries is not used as a template for new ones.
- A future legacy-v1 email consolidation can depend on `UserOrg.Shared.Email.IEmailSender`
  directly, without a `Core` reference.
- The next block implements `HttpEmailSender` in `Userorg.Adapter`, the first outbound HTTP client
  owned by that project.
