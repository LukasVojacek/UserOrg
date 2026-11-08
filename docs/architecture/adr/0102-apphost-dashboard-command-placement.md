# ADR 0102 — AppHost dashboard command placement: highlighted is for the one command a developer runs by hand

- Status: Accepted
- Date: 2026-09-10
- Context: `src/UserOrg.AppHost` and the Aspire 13.5.3 dashboard; written from T-071, which added
  `vault-setup` and `db-lifecycle` and reworked the SQL commands.
- Relates to: [`0069-testcontainers-only-e2e-retire-aspire-boot.md`](0069-testcontainers-only-e2e-retire-aspire-boot.md)
  (the AppHost is no longer booted by the test suite, so the dashboard is the only place these commands are
  exercised).

## Context

Every resource command in this AppHost is registered with `WithCommand(name, displayName, commandOptions,
executeCommand)`. `CommandOptions` carries four placement-relevant members and nothing that names a toolbar:

- `IsHighlighted` — `true` renders the command as its own button on the resource's row and detail toolbar;
  `false` (the default) puts it in the resource's overflow (`…`) menu.
- `UpdateState` — a callback returning `ResourceCommandState.Enabled`, `Disabled` or `Hidden`, re-evaluated on
  every resource snapshot. This is what decides whether a placed command can be clicked *now*.
- `Visibility` — `ResourceCommandVisibility.UI`, `Api` or `None`, which decides whether the command is offered
  to a human at all or only over the command API.
- `Arguments` — `InteractionInput`s, which turn the command into a dialog rather than a one-click action.

Placement was being decided per command as each one was written, and the same decision was re-argued in T-071
for six commands across three files. The AppHost is developer-facing UI: an inconsistent toolbar costs a
developer a wrong click on a destructive database command.

## Decision

1. **`IsHighlighted = true` is reserved for a command that is part of the normal run** — one a developer is
   expected to press by hand on an ordinary day, on the resource that owns it. Everything else stays in the
   overflow menu, which is the default and therefore requires no argument.

2. **A command is registered on the resource it acts on**, not on the resource that happens to compose it.
   `vault-setup` mutates the Vault container, so it lives on `Vault`; the SQL commands mutate the database
   inside the SQL Server container, so they live on that container; `gettoken` calls the gateway's own
   endpoint, so it lives on `gateway`.

3. **Destructive or rarely used commands are never highlighted**, and a destructive one carries either a
   `ConfirmationMessage` or a required `Arguments` choice, so it cannot be triggered by a single stray click.

4. **`UpdateState` gates on the owning resource's health**, not on placement. Every command in this AppHost
   returns `Enabled` when `context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy` and `Disabled`
   otherwise. `Hidden` is not used: a command that disappears when a resource is unhealthy reads as a missing
   feature, whereas a disabled one reads as "not yet".

5. **`Visibility` is left at its default.** No command in this AppHost is API-only or hidden; if one ever is,
   that is a change to this ADR, not a local decision.

## What this means for what T-071 shipped

| command | resource | placement | why |
|---|---|---|---|
| `vault-setup` | `Vault` | highlighted | every run that needs a seeded dev Vault presses it; its `Mode` argument defaults to the non-destructive `Keep` |
| `gettoken` | `gateway` | highlighted | the local-Keycloak branch's everyday action — a developer needs a bearer token to call anything by hand |
| `db-lifecycle` | SQL Server container | overflow | four modes, two of which drop the database; the required `Mode` choice is the guard |
| `RestoreDevDatabase` | SQL Server container | overflow | overwrites the target database from a backup file |
| `SnapshotDatabase` | SQL Server container | overflow | replaces the previous dev snapshot |
| `RevertToSnapshot` | SQL Server container | overflow | discards every change since the snapshot |

## Consequences

- The dashboard shows at most one highlighted button per resource, so the toolbar stays readable and the
  highlighted buttons keep meaning "this is the thing you press".
- A new command's placement is no longer an open question: highlighted only if it clears rule 1, otherwise
  overflow.
- The convention lives in one place instead of in six `CommandOptions` literals. `module-conventions.md` is
  unchanged; it documents module structure, not dashboard affordances.
- Nothing here is enforced by a test. The AppHost test suite asserts source syntax, and it can see
  `IsHighlighted` but not whether the placement is *right*; rules 1-3 are review criteria.
