# Multitenancy Implementation Checklist

**Phase:** Ready to implement  
**Estimated effort:** ~5-7 days for 1 developer  
**Risk level:** Low (no global filters, incremental ownership checks)

---

## Pre-Implementation

- [ ] Review `MULTITENANCY_DESIGN.md` with team
- [ ] Set up code review checklist for ownership verification
- [ ] Test DB backfill script on staging environment copy
- [ ] Identify all existing write operations that need ownership checks

---

## Phase 1: Database Migration

**Estimated time:** 2 hours  
**Risk:** Low (nullable column, reversible)

- [ ] Create EF Core migration: `Add_Project_ProviderId_FK`
  - [ ] Add nullable `ProviderId` column to `Projects` table
  - [ ] Create foreign key constraint to `Providers.Id`
- [ ] Write backfill script that populates `ProviderId` from location chain
  ```sql
  UPDATE p SET p.ProviderId = (
      SELECT TOP 1 l.ProviderId
      FROM AssignedProjectResourceToProjects apr
      JOIN ProjectResources pr ON apr.ProjectResourceId = pr.Id
      JOIN AssignedLocations al ON al.ProjectResourceId = pr.Id
      JOIN Locations l ON al.LocationId = l.Id
      WHERE apr.ProjectId = p.Id AND al.IsDeleted = 0
  )
  FROM Projects p;
  ```
- [ ] Test backfill on production data copy (staging)
  - [ ] Verify all projects with resources get `ProviderId`
  - [ ] Verify projects without resources get `NULL`
- [ ] Apply migration to development DB
- [ ] Apply migration to staging DB
- [ ] Document rollback procedure (DROP CONSTRAINT, DROP COLUMN)

---

## Phase 2: Domain Entity + EF Configuration

**Estimated time:** 1 hour  
**Risk:** Low (straightforward property addition)

- [ ] Edit `src/Core/Domain/Entities/Project.cs`
  - [ ] Add property: `public Guid? ProviderId { get; set; }`
  - [ ] Add navigation property: `public Provider? Provider { get; set; }`
- [ ] Add EF configuration in `src/Core/Infrastructure.DataAccess/Contexts/DatabaseContext.cs` (or dedicated `ProjectConfiguration`)
  ```csharp
  entity.HasOne(p => p.Provider)
        .WithMany()
        .HasForeignKey(p => p.ProviderId)
        .IsRequired(false);
  ```
- [ ] Verify build succeeds
- [ ] Run EF Core migration validation: `dotnet ef migrations validate`

---

## Phase 3: Service Layer — Ownership Verification

**Estimated time:** 3-4 hours  
**Risk:** Medium (must find all write operations)

### Step 1: Add ownership check helper

- [ ] In `src/Core/Application/UserOrg/Services/ProjectService.cs`, add private static method:
  ```csharp
  private static void EnsureCallerOwnsProject(Project project, Guid? callerProviderId)
  {
      if (callerProviderId is null) 
          return;  // PlatformAdmin bypass
      
      if (project.ProviderId != callerProviderId)
          throw new ForbiddenException($"Access denied. Project belongs to provider {project.ProviderId}");
  }
  ```

### Step 2: Apply to ProjectService write operations

- [ ] `UpdateProjectAsync` — add check after fetching project
- [ ] `DeleteProjectAsync` — add check after fetching project
- [ ] `EditAssignedUserSystemRole` (both overloads) — add check
- [ ] `AssignedProjectResourceToProjectAsync` — add check
- [ ] `UnAssignedProjectResourceToProjectAsync` — add check
- [ ] `AssignedUserToProjectWithRoleAsync` — add check
- [ ] `UnAssignedUserFromProjectAsync` — add check
- [ ] `InsertProjectAsync` — stamp `project.ProviderId = callerProviderId` if not null

### Step 3: Update method signatures

- [ ] Add `Guid? callerProviderId` parameter to each method
- [ ] Update callers in endpoint classes to pass `callerProviderId` from JWT
  ```csharp
  httpContext.TryGetProviderId(out var callerProviderId);
  await _projectService.UpdateProjectAsync(projectId, updates, callerProviderId, cancellationToken);
  ```

### Step 4: Similar pattern for other services

- [ ] `ProviderService` — `UpdateProviderAsync`, `DeleteProviderAsync`
- [ ] `LocationService` — `InsertLocationAsync`, `UpdateLocationAsync`, `DeleteLocationAsync`
- [ ] `ProjectResourceService` — `UpdateProjectResourceAsync`, `DeleteProjectResourceAsync`
- [ ] `ProjectResourceAssignmentService` — `InsertProjectResourceAssignmentAsync`, `UpdateProjectResourceAssignmentAsync`, `DeleteProjectResourceAssignmentAsync`

---

## Phase 4: Repository Layer — Tenant-scoped Queries

**Estimated time:** 1-2 hours  
**Risk:** Low (additive only, no filter removal)

- [ ] Edit `src/Core/Infrastructure.DataAccess/Repositories/ProjectRepository.cs`
  - [ ] Update `GetAllBySpecificationsAsync` to accept optional `Guid? providerId` parameter
  - [ ] Add `.Where(p => p.ProviderId == providerId)` if `providerId.HasValue`
- [ ] Update `GetProjectsForProvider` to use direct FK lookup instead of 5-hop join (optional optimization)
- [ ] Run unit tests: `dotnet test ProjectRepositoryTests`

---

## Phase 5: API Endpoints — ProviderId Propagation

**Estimated time:** 2-3 hours  
**Risk:** Low (passes-through, no business logic)

- [ ] Edit `src/Core/API/API/ProjectEndpoint.cs`
  - [ ] Add `httpContext.TryGetProviderId(out var callerProviderId)` to each write route
  - [ ] Pass `callerProviderId` to `_projectService` methods
  - [ ] Example:
    ```csharp
    [HttpPut("{shortName}")]
    public async Task<IResult> UpdateProject(
        [FromRoute] ProjectShortName shortName,
        [FromBody] UpdateProjectRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        httpContext.TryGetProviderId(out var callerProviderId);
        
        await _projectService.UpdateProjectAsync(
            shortName, 
            request, 
            callerProviderId,  // ← Pass caller's provider context
            ct);
        
        return Results.NoContent();
    }
    ```

- [ ] Similar updates for:
  - [ ] `ProviderEndpoint`
  - [ ] `LocationEndpoint`
  - [ ] `ProjectResourceEndpoint`

---

## Phase 6: ReadOnly Toggle Feature

**Estimated time:** 2-3 hours  
**Risk:** Low (new endpoints, no changes to existing)

### Step 1: New service methods

- [ ] Add to `IProjectService`:
  ```csharp
  Task SetProjectModeAsync(ProjectShortName shortName, EOperationMode mode, Guid? callerProviderId, CancellationToken ct);
  ```

- [ ] Implement in `ProjectService`:
  ```csharp
  public async Task SetProjectModeAsync(ProjectShortName shortName, EOperationMode mode, Guid? callerProviderId, CancellationToken ct)
  {
      var project = await _repo.GetByShortNameAsync(shortName, ct);
      
      // Ownership check
      EnsureCallerOwnsProject(project, callerProviderId);
      
      // Guard: cannot unlock system-managed records
      if (project.Mode == EOperationMode.ReadOnly && project.ServiceName != "Lexis")
          throw new RecordReadOnlyException("Cannot unlock system-managed records");
      
      project.Mode = mode;
      // Keep ServiceName unchanged
      
      await _context.SaveChangesAsync(ct);
  }
  ```

- [ ] Similar methods for:
  - [ ] `ILocationService.SetLocationModeAsync`
  - [ ] `IProviderService.SetProviderModeAsync`

### Step 2: New API endpoints

- [ ] Add to `ProjectEndpoint`:
  ```csharp
  [HttpPatch("{shortName}/mode")]
  public async Task<IResult> SetProjectMode(
      [FromRoute] ProjectShortName shortName,
      [FromBody] SetEntityModeRequest request,
      HttpContext httpContext,
      CancellationToken ct)
  {
      httpContext.TryGetProviderId(out var callerProviderId);
      
      await _projectService.SetProjectModeAsync(shortName, request.Mode, callerProviderId, ct);
      
      return Results.NoContent();
  }
  ```

- [ ] Similar endpoints for:
  - [ ] `LocationEndpoint` — `PATCH /{id}/mode`
  - [ ] `ProviderEndpoint` — `PATCH /{id}/mode`

### Step 3: Request/Response DTOs

- [ ] Create `SetEntityModeRequest`:
  ```csharp
  public record SetEntityModeRequest(EOperationMode Mode);
  ```

- [ ] Extend response DTOs with `LockedBy` computed property:
  ```csharp
  public record ProjectModel(
      ProjectId Id,
      string Name,
      // ... existing fields ...
      string Mode,
      string? LockedBy  // = ServiceName when Mode == ReadOnly, else null
  );
  ```

---

## Phase 7: Forge.Core Permissions

**Estimated time:** 1 hour  
**Risk:** Low (configuration only)

- [ ] In ForgeDb, register new permission keys:
  - [ ] `perm:provider:lock`
  - [ ] `perm:provider-location:lock`
  - [ ] `perm:provider-project:lock`
  - [ ] `perm:provider-resource:lock`

- [ ] Assign `perm:provider:*` permissions to `ProviderAdmin` role

- [ ] Document in Forge.Core permission reference

---

## Phase 8: Testing

**Estimated time:** 3-4 hours  
**Risk:** Low (comprehensive test coverage increases confidence)

### Unit Tests

- [ ] `EnsureCallerOwnsProject_SameProvider_DoesNotThrow`
- [ ] `EnsureCallerOwnsProject_DifferentProvider_Throws`
- [ ] `EnsureCallerOwnsProject_PlatformAdmin_DoesNotThrow`
- [ ] `SetProjectMode_LockingTenantData_Succeeds`
- [ ] `SetProjectMode_UnlockingSystemData_Throws`

### Integration Tests

- [ ] `ProviderAdminCanUpdateOwnProject`
- [ ] `ProviderAdminCannotUpdateForeignProject` (403)
- [ ] `PlatformAdminCanUpdateAnyProject` (bypass null providerId)
- [ ] `ProviderAdminCanToggleOwnProjectMode`
- [ ] `ProviderAdminCannotToggleWaldurLockedProject` (409)
- [ ] `GET /Project returns all projects regardless of caller's provider`

### Manual Testing

- [ ] Test in development environment:
  - [ ] Create project as ProviderAdmin A
  - [ ] Verify `Projects.ProviderId = A`
  - [ ] Try to update as ProviderAdmin B → 403
  - [ ] Try to update as PlatformAdmin → 200
- [ ] Test ReadOnly toggle:
  - [ ] Lock project → Mode = ReadOnly
  - [ ] Try to update locked project → 409
  - [ ] Unlock project → Mode = Editable
  - [ ] Update succeeds

---

## Phase 9: Documentation & Deployment

**Estimated time:** 1-2 hours  
**Risk:** Low (no runtime risk)

- [ ] Update API documentation (Swagger/OpenAPI) with new PATCH endpoints
- [ ] Add examples to README:
  - [ ] How to toggle ReadOnly mode
  - [ ] How to interpret `LockedBy` field in responses
- [ ] Update runbooks for support team:
  - [ ] How to identify which provider owns an entity
  - [ ] When to use admin override vs normal operations
- [ ] Create deployment guide:
  - [ ] Run migration on production
  - [ ] Monitor logs for ForbiddenException spikes
  - [ ] Verify PlatformAdmin can still manage system

---

## Rollback Plan

If critical issues are discovered:

1. **Revert code changes** (git revert)
2. **Rollback DB migration:**
   ```sql
   ALTER TABLE Projects DROP CONSTRAINT FK_Projects_Providers;
   ALTER TABLE Projects DROP COLUMN ProviderId;
   ```
3. **Redeploy previous version**

**Time to rollback:** ~30 minutes

---

## Sign-off Checklist

- [ ] Code review completed (all ownership checks verified)
- [ ] All tests pass locally
- [ ] Staging deployment successful
- [ ] Integration tests pass on staging
- [ ] Audit logging works correctly
- [ ] Team understands new permission model
- [ ] Support/ops runbooks updated
- [ ] Ready for production deployment

---

## Success Criteria

After implementation:

- [ ] ProviderAdmin can only modify own provider's data ✅
- [ ] PlatformAdmin can read all data, modify with explicit override ✅
- [ ] ReadOnly toggle works for tenant-owned entities ✅
- [ ] System-managed (Waldur) locks cannot be bypassed by tenant ✅
- [ ] Audit trail records all admin overrides ✅
- [ ] Zero unintended data access incidents ✅
- [ ] Team comfortable with new multitenancy model ✅

