# ProjectResource State Transitions

## State Diagram

```
		   Create()
			  |
			  v
			DRAFT
			  |
		CheckHeappeStep (1)
			  |
			  v
  InitializeProjectResourceStep (2)
			  |
			  v
	InitializeHeappeStep (3)
			  |
			  v
  WaitingForCreationAccounts (4)
			  |
			  v
FinalizeCreationProjectResourceStep (5)
			  |
			  v
		   PUBLISHED (100)
			  |
		(Success path)

		   DRAFT ──────────────────┐
			  |                    |
		 [any error]               |
			  |                    |
			  v                    v
		   FAILED (-1)          DELETED (200)
								   ^
								   |
						[explicit delete]

```

## State Definitions

| State | Value | Description | Workflow Phase |
|-------|-------|-------------|-----------------|
| Failed | -1 | Resource provisioning failed; marked for rollback | Error |
| Draft | 0 | Resource created, awaiting provision workflow | Initial |
| CheckHeappeStep | 1 | Checking Heappe service status | Provisioning |
| InitializeProjectResourceStep | 2 | Initializing resource in project system | Provisioning |
| InitializeHeappeStep | 3 | Initializing resource in Heappe | Provisioning |
| WaitingForCreationAccounts | 4 | Waiting for account creation (user interaction) | Provisioning |
| FinalizeCreationProjectResourceStep | 5 | Finalizing resource creation | Provisioning |
| Published | 100 | Resource is fully provisioned and usable | Final/Success |
| Deleted | 200 | Resource is soft-deleted | Final/Archived |

## Transition Matrix

| From | To | Valid | Condition / Notes |
|------|----|----|---------------|
| Draft | CheckHeappeStep | ✓ | Start provisioning workflow |
| CheckHeappeStep | InitializeProjectResourceStep | ✓ | Heappe check passed |
| InitializeProjectResourceStep | InitializeHeappeStep | ✓ | Project resource initialized |
| InitializeHeappeStep | WaitingForCreationAccounts | ✓ | Heappe initialized, waiting for manual action |
| WaitingForCreationAccounts | FinalizeCreationProjectResourceStep | ✓ | Accounts created, finalization starts |
| FinalizeCreationProjectResourceStep | Published | ✓ | Resource fully provisioned and usable |
| * | Failed | ✓ | Any state → Failed on error; marks rollback |
| * | Deleted | ✓ | Any state → Deleted (explicit soft-delete) |
| Failed | * | ✗ | No transitions FROM Failed (end state) |
| Published | * | ✗ | Published → Deleted only (no de-publish) |
| Deleted | * | ✗ | No transitions FROM Deleted (end state) |

## Business Rules

1. **Creation**: `ProjectResource.Create()` initializes with `State = Draft`.
   - A resource is usable the moment it exists (`ProjectResourceActivatedEvent` raised immediately).
   - Workflow handlers immediately transition state to provisioning phases.

2. **Provisioning Workflow**: Multi-step handler-driven state machine:
   - `InitializeProjectResourceHandler` → Draft to provisioning states
   - `InitializeHeappeHandler` → Heappe interaction states
   - `FinalizeCreationProjectResourceHandler` → Finalization & transition to Published
   - WorkflowEngine orchestrates sequencing and retries.

3. **Error Handling**: `MarkFailed()` can be called from any state to mark resource as Failed.
   - Rollback logic should clean up partial provisioning.
   - Failed resources are archived (not deleted).

4. **Publication**: `Publish()` transitions FinalizeCreationProjectResourceStep → Published.
   - Triggers auto-assignment to project if request is pending.
   - Sends notifications to provider support email and primary investigator.

5. **Deletion**: `Delete()` transitions any state → Deleted.
   - Sets `IsDeleted = true` (persistence) AND `State = Deleted` (business).
   - Typically called by provider admin when resource is no longer needed.

6. **Immutability of Name/Provider**: Cannot rename or re-parent a resource; natural key is immutable.

## Integration Points

- **CreateResourceEndpoint**: Creates resource with `State = Draft`.
- **WorkflowEngine**: Transitions states during async provisioning (handlers call `UpdateState()`).
- **PublishResourceEndpoint**: Calls `resource.Publish()` to transition → Published.
- **DeleteResourceEndpoint**: Calls `resource.Delete()` to transition → Deleted.
- **Queries**: `ResourceDetailsQuery`, `SyncDetailsQuery` expose `State` in response DTOs.

## Error Handling

| Error Code | Scenario | HTTP Status |
|------------|----------|-------------|
| resource.invalid_state_for_publish | State is not FinalizeCreationProjectResourceStep | 400 Bad Request |
| resource.provisioning_failed | State is Failed | 400/409 depending on context |

## Key Differences from Project

- **Granularity**: 8+ provisioning states vs. Project's 3 lifecycle states
- **Automation**: ProjectResource transitions are orchestrated by WorkflowEngine (durable workflow); Project transitions are explicit method calls
- **Recovery**: ProjectResource has `Failed` as a rollback marker; Project has no intermediate error state
- **Duration**: ProjectResource provisioning is long-running (hours/days); Project transitions are immediate

## Future Enhancements

1. Add `PublishFailed()` method for explicit failure handling if needed.
2. Implement `Update()` method for Draft → Draft state changes (metadata updates before provisioning).
3. Consider expiration-based archival (e.g., EndDate → auto-expired state, separate from Deleted).
