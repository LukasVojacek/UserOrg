# Context Map

This repository hosts the UserOrg domain plus one reusable, domain-agnostic library
that has its own bounded context and glossary.

## Contexts

- [UserOrg](./CONTEXT.md) — organisation, project, and access-management backend (the product domain).
- [WorkflowEngine](./src/Modules/Libraries/WorkflowEngine.Core/CONTEXT.md) — a generic, NuGet-publishable
  library for asynchronous, typed state-machine orchestration. Knows nothing about the UserOrg domain.

## Relationships

- **UserOrg → WorkflowEngine** (consumer → library): UserOrg drives long-running flows by defining
  a `Strategy` and its `Step` handlers and registering them in DI. The library never references UserOrg types.
- **Naming overlap — deliberate, kept apart by context.** Both contexts use the word **Request**, but
  they are different concepts:
  - WorkflowEngine **Request** = one *instance* of a running workflow (the orchestration aggregate).
  - UserOrg **Request** = a pending onboarding/resource ask that moves through Pending → Accepted | Rejected.

  They share no code and no table. When prose spans both contexts, qualify the term
  (e.g. "workflow Request" vs "onboarding Request").
