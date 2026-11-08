# Project State Transitions

## State Diagram

```
		   Create()
			  |
			  v
		   ACTIVE
		   /    \
	  Expire()   Delete()
		  |         |
		  v         v
	   EXPIRED    DELETED
		   \        /
			(archived)
```

## State Definitions

| State   | Value | Description                                          | Editable? |
|---------|-------|------------------------------------------------------|-----------|
| Active  | 1     | Project is operational, within validity period       | Yes*      |
| Expired | 2     | Project is past EndDate; archived, non-operational   | No**      |
| Deleted | 100   | Project is soft-deleted; hidden from regular queries | No**      |

*Editability depends on `Mode` (EOperationMode.Editable vs. ReadOnly)
**State = Expired or Deleted blocks all modifications regardless of Mode

## Transition Matrix

| From    | To      | Valid | Condition / Notes                           |
|---------|---------|-------|---------------------------------------------|
| Active  | Expired | ✓     | Any Active → Expired (typically via cron)  |
| Active  | Deleted | ✓     | Any Active → Deleted (explicit delete)     |
| Expired | Deleted | ✓     | Expired → Deleted (cleanup)                |
| Deleted | *       | ✗     | No transitions FROM Deleted allowed        |
| Expired | Active  | ✗     | Cannot reactivate expired projects         |
| Expired | Expired | ⚠     | No-op (idempotent Expire call)             |
| Active  | Active  | ⚠     | No-op (no-op state retention)              |

## Business Rules

1. **Creation**: `Project.Create()` initializes with `State = Active`.
   - Project is immediately available for user assignment and resource allocation.
   - Mode (Editable/ReadOnly) is orthogonal and determined at creation.

2. **Expiration**: `Project.Expire()` transitions Active → Expired.
   - Typically invoked by a scheduled job when `DateTime.UtcNow > EndDate`.
   - Prevents all further modifications, including Mode changes.
   - Soft-deletes are still allowed (to clean up).

3. **Deletion**: `Project.Delete()` transitions any state → Deleted.
   - Cascades soft-delete to project roles (UserPermission rows).
   - Sets `IsDeleted = true` (persistence-level) AND `State = Deleted` (business-level).
   - Raises `ProjectDeletedEvent`.

4. **Read-Only Checks**: `EnsureNotReadOnly()` now validates:
   - Mode != ReadOnly
   - State != Deleted
   - State != Expired

5. **No Project Restoration**: Deleted or Expired projects cannot be reactivated via the API.
   - Data is retained (soft-delete); restoration requires manual database surgery or a separate admin endpoint.

## Integration Points

- **CreateProjectEndpoint**: Creates project with `State = Active`.
- **DeleteProjectEndpoint**: Calls `project.Delete()` → State becomes Deleted.
- **UpdateProjectEndpoint**: Any update fails if State is Expired or Deleted.
- **AssignUser/RemoveUser**: Fail if project is Expired or Deleted.
- **AssignResource/RemoveResource**: Fail if project is Expired or Deleted.
- **Scheduled Expiration Job** (future): Calls `project.Expire()` on all Active projects where `EndDate < now`.
- **Queries**: `ProjectDetailsQuery` must include State in response DTOs.

## Error Handling

| Error Code | Scenario | HTTP Status |
|------------|----------|-------------|
| project.read_only | Project Mode is ReadOnly | 400 Bad Request |
| project.deleted | Project State is Deleted | 400 Bad Request |
| project.expired | Project State is Expired | 400 Bad Request |
| project.cannot_expire_deleted | Expire called on Deleted project | 400 Bad Request |

## Symmetry with ProjectResource

ProjectResource has a more granular workflow (Draft, CheckHeappeStep, InitializeProjectResourceStep, …, Published, Failed, Deleted).
Project uses a simplified lifecycle since it doesn't go through multi-step provisioning:

- **ProjectResource**: Provisioning workflow (8+ states)
- **Project**: Simple lifecycle (3 states: Active, Expired, Deleted)

Both use `IsDeleted` flag for persistence-level soft-delete and expose `State` in DTOs.
