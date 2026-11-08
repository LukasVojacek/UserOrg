# ADR 0013 - WorkflowEngine is a standalone bounded context

- Date: 2026-06-09
- Status: accepted

## Decision

`WorkflowEngine` (the projects under `src/Modules/Libraries/WorkflowEngine.*`) is treated as a
**generic, domain-agnostic library with its own bounded context and glossary** — not as part of the
UserOrg domain vocabulary. The repository is therefore a **multi-context** repository: a root
[`CONTEXT-MAP.md`](../../../CONTEXT-MAP.md) lists the two contexts, and the library carries its own
[`CONTEXT.md`](../../../src/Modules/Libraries/WorkflowEngine.Core/CONTEXT.md) glossary alongside the
root UserOrg [`CONTEXT.md`](../../../CONTEXT.md).

A direct consequence: the word **Request** legitimately means two different things in the two contexts,
and the collision is **resolved by the context boundary, not by renaming**:

- WorkflowEngine **Request** (`RequestAggregate`, `IRequestRepository`, `RequestStatus`, `StepRequest`,
  the `Requests` table) = one *instance* of a running workflow.
- UserOrg **Request** (`Core.Domain.Request`) = a pending onboarding/resource ask that moves through
  Pending → Accepted | Rejected.

The engine's internal `Request` types are **kept as-is**; we do not rename them to avoid the overlap.
Within the engine's own vocabulary the canonical terms are fixed: **Request** = the instance,
**Flow** = the state-machine definition/progression (never the instance), and **Job** is dropped.

## Context

- The engine lives under `src/Modules/Libraries/` (not `Core`), ships zero concrete strategies, has no
  HTTP or host-framework dependency, owns its own `WorkflowDbContext` and schema, and is described in its
  design docs as a "self-contained, NuGet-publishable library". By every structural signal it is a
  separate, reusable component rather than a feature of the UserOrg domain.
- Before this decision the vocabulary was inconsistent in three ways that hurt comprehension:
  1. **Cross-context collision** — `WorkflowEngine.RequestAggregate` vs `Core.Domain.Request`, with the
     root glossary defining "Request" only as the onboarding ask.
  2. **Module-name split** — namespaces, DI (`AddWorkflowEngine`), and the config section all say
     *WorkflowEngine*, but the options type was `RequestsModuleOptions` and the docs called it the
     "Requests module".
  3. **Instance named three ways inside the library** — *Request* (types/table), *Flow*
     (`SetFlowState`, the public `MarkFlowFailedAsync`, "flow-level status"), and *Job* (`jobId`).
- A bounded-context boundary is the standard way to let the same word mean different things in different
  contexts without forcing one of them to rename. The alternative — folding the engine into the UserOrg
  domain — would make the collision a genuine in-context ambiguity that *would* require renames.

## Considered options

- **Standalone generic library / separate bounded context (chosen).** Keep the engine's `Request`
  vocabulary; document the boundary via a context map and a library glossary; qualify the term only when
  prose spans both contexts ("workflow Request" vs "onboarding Request"). Lowest code churn, and it
  matches the engine's actual structure and packaging intent.
- **Fold the engine into the UserOrg domain.** Would make `WorkflowEngine.Request` vs
  `Core.Domain.Request` a real ambiguity and force renaming the engine's instance concept (e.g. to
  `Run`/`WorkflowInstance`) across the whole type surface, the table, and migrations. Rejected: high
  churn, and it contradicts the library's domain-agnostic, reusable design.
- **Hybrid (library form, but only ever consumed by UserOrg).** Keep the generic shape but treat the
  glossary as shared. Rejected: gives up the clean boundary while keeping most of its cost, and blurs the
  "knows nothing about UserOrg" invariant.

## Consequences

- The repository is now **multi-context**: tooling and contributors read `CONTEXT-MAP.md` first, then the
  per-context glossary. New workflow-engine terms are captured in the library `CONTEXT.md`, not the root.
- The engine's `Request*` type names **stay**; no rename is performed to dodge the overlap. Reviewers
  should not "fix" the duplication — it is deliberate and bounded.
- Documentation and prose that span both contexts must **qualify** the word Request. Unqualified
  "Request" inside engine code/docs means the workflow instance; inside UserOrg domain code/docs it means
  the onboarding ask.
- This decision is the prerequisite for the in-library vocabulary clean-up (Flow = definition not
  instance; drop Job; `RequestsModuleOptions` → `WorkflowEngineOptions`; `MarkFlowFailedAsync` →
  `MarkRequestFailedAsync`; `SetFlowState` → `SetCurrentState`), which is safe to perform now because the
  engine has **no production consumer** yet (only a sample app and tests).
