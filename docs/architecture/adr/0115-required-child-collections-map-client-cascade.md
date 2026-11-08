# ADR 0115 - Required child collections in the v2 model map ClientCascade

- Date: 2026-10-02
- Status: accepted
- Relates: ADR-0032 (the single system permission, amended by this change), ADR-0018 (deactivation does not
  cascade), ADR-0047 (how modular tables ship, unchanged)

## Context

`PUT v2/Users/{id}/ProviderAdmin/{provider}` and `DELETE v2/Users/{id}/ProviderAdmin` answered 500 (#386).
`User.PromoteToProviderAdmin` and `User.ResetToPlatformUser` cleared `_systemPermissions` and added a new row.
The FK behind that collection is required and mapped `NoAction`, so `SaveChangesAsync` threw
`InvalidOperationException` for the severed association. c8ec0a4b fixed the same class of defect for
`AssignedLocation._assignedLocationValues` with `ClientCascade` and a snapshot-only migration.

Four further required child collections were still `NoAction`: `Location._locationAllocations`,
`Provider._providerLocations`, `Project._assignedUsers` and `Project._assignedResources`. None is a live bug
today, but the first caller that removes a child from one of them fails the same way. `_assignedUsers` and
`_locationAllocations` were also mapped a second time from the dependent side without a navigation, so their
delete behaviour depended on configuration order.

## Decision

**In the v2 model, a required child collection maps `ClientCascade`, or database `Cascade`.** A required child
collection is a required FK whose principal has a collection navigation to the dependent. The database FK stays
NO ACTION, so the change ships as a snapshot-only migration with empty `Up` and `Down`
(`20261002141150_RequiredCollectionsClientCascade`). Each such relationship is mapped once, from the
principal's collection, keeping its constraint name. `RequiredCollectionDeleteBehaviorTests` walks the model and
enforces the rule.

There are two exceptions.

1. `User._systemPermissions` stays `NoAction`. Promote and reset change the existing row in place through
   `UserPermission.ChangeRole` (ADR-0032, decision 6). `UserPermission` is shared with `Project._memberRoles`,
   and a soft-deleted system permission would stay visible to v1
   (`UserPermissionRepository.GetSystemPermissionsForUserAsync`, no `IsDeleted` filter) and to the Waldur
   `UserAssignedHandler`.
2. `Request._requestActionLinks` keeps database `Cascade`. Switching it to `ClientCascade` would be a real
   database FK change.

Scope: domain deactivation, setting `IsDeleted` on the principal, never cascades, consistent with ADR-0018.
`IRepository.Delete` on a principal cascades to its loaded required children.

## Consequences

- Removing a child from a required collection no longer throws on `SaveChanges`. The child is deleted.
- Unproven: whether a soft-deletable child removed from the collection survives `SaveChanges` as a soft delete
  through `ApplyConventions` (`DatabaseContext.UnitOfWork.cs`). EF's `HandleConceptualNulls` can re-mark it
  Deleted after the convention ran. The first caller that removes such a child adds a SQL Server behaviour test.
- A client cascade that reaches a non-soft-deletable child is a hard delete. Today that is
  `AssignedLocationValue` under Provider, Location, AssignedLocation. It is forward-only: a code rollback does
  not restore the rows.
- The database schema is unchanged. `CoreModelSnapshotTests` cannot tell `ClientCascade` from `NoAction`,
  because both are NO ACTION in the relational model, so the metadata test is the guard.

Rejected: comments on the configurations only, amending ADR-0047 (it governs how modular tables ship, not
mapping), and `ClientCascade` on `_systemPermissions` with the old clear-and-add (it soft-deletes the system
permission that v1 and Waldur still read).
