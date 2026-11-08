# Waldur ↔ HEAppE — Detailed Sequence Diagram (Single Iteration)

This diagram describes the specific HTTP endpoints invoked during a single synchronization iteration between UserOrg, Waldur and HEAppE. The diagram does not include authentication details (tokens/headers) but lists exact paths and possible alternative branches of behavior.

```mermaid
sequenceDiagram
  autonumber
  participant Userorg as UserOrg (Orchestrator / Worker)
  participant Collector as Waldur Collector (HttpClient)
  participant WaldurAPI as Waldur Platform API
  participant HEAPPE as HEAppE API
  participant SyncQueue as Sync Queue (Channel)
  participant Consumer as Background Consumer (ChannelWorker)
  participant Telemetry as Telemetry / Logging

  Note over Userorg,Collector: Start iteration (timer or manual trigger)

  Userorg->>Collector: 1) Request project list
  Collector->>WaldurAPI: GET {ProjectsEndpoint}    <-- e.g. /api/projects/
  WaldurAPI-->>Collector: 200 OK (page 1 of N)
  Collector->>WaldurAPI: GET {ProjectsEndpoint}?page=2 ... pagination
  alt Error (non-2xx)
    Collector->>Telemetry: Record error, add to DLQ
    Telemetry-->>Userorg: Error notification
  end

  Note over Collector: For each project — fetch resources and users
  Collector->>WaldurAPI: GET {MarketplaceResourcesEndpoint}?project_uuid={projectId}   <-- e.g. /api/marketplace-resources/
  Collector->>WaldurAPI: GET {ProjectsEndpoint}/{projectId}/list_users/    <-- project users list
  Collector->>WaldurAPI: GET {MarketplacePublicOfferingsEndpoint}/{offeringUuid}/  <-- offering details

  Collector-->>Userorg: 2) Push AggregateData (ProjectDto, ResourceDto, UserDto)

  Note over Userorg: Mapping and per-resource upsert (atomic transaction per resource)
  Userorg->>Userorg: 3) Map payload -> create Project, ProjectResource, AssignedLocation, AssignedUsers
  Userorg->>Userorg: 4) Upsert into UserOrg (SaveChanges in transaction)
  alt DB save error
    Userorg->>Telemetry: Log error, rollback transaction, increment failure counter
    Telemetry-->>Userorg: Alert
    Userorg->>Collector: Mark resource as failed (retry in next iteration)
  end

  Note over Userorg,HEAPPE: If resource contains HEAppE configuration (AssignedLocationsValues include HEAppE URI/Identifier)
  Userorg->>HEAPPE: 5) Check project existence in HEAppE
  HEAPPE-->>Userorg: GET /heappe/Management/ProjectsByAccountingStrings?AccountingString={resourceName} -> 200 OK (list) or NotFound

  alt Project not found
    Userorg->>HEAPPE: POST /heappe/Management/Project  (CreateProject)   body: { name, description, startDate, endDate, usageType, ... }
    HEAPPE-->>Userorg: 201 Created / 200 OK -> ProjectModel (projectId)
    Note right of HEAPPE: possible cluster validation before creation (GetClusters)
  else Project found
    Userorg->>HEAPPE: PUT /heappe/Management/Project  (UpdateProject)  body: { id, name, description, ... }
    HEAPPE-->>Userorg: 200 OK / updated ProjectModel
  end

  Note over Userorg,HEAPPE: Next, ensure project assignment to cluster
  Userorg->>HEAPPE: GET /heappe/Management/ProjectAssignmentToClusters?ProjectId={projectId}
  HEAPPE-->>Userorg: 200 OK (assignments list)
  alt Assignment missing for target cluster
    Userorg->>HEAPPE: POST /heappe/Management/ProjectAssignmentToCluster  body: { clusterId, projectId, scratchStoragePath, projectStoragePath }
    HEAPPE-->>Userorg: 201 Created / 200 OK
  else Assignment exists but storage paths differ
    Userorg->>HEAPPE: PUT /heappe/Management/ProjectAssignmentToCluster  body: { clusterId, projectId, scratchStoragePath, projectStoragePath }
    HEAPPE-->>Userorg: 200 OK
  end

  alt HEAppE errors (cluster missing, validation failed)
    HEAPPE-->>Userorg: 4xx/5xx error
    Userorg->>Userorg: rollback transaction (if during EnsureProjectResourcesAsync)
    Userorg->>Telemetry: Log error, mark resource as failed
    Userorg->>Collector: Report failure to DLQ
  end

  Note over Userorg,Telemetry: Record metrics and final state
  Userorg->>Telemetry: Emit phase durations, success/failure, counters

  Note over Userorg: Transaction committed and work item enqueued for background sync
  Userorg->>SyncQueue: Enqueue WorkItem(ProjectShortName, ResourceName)
  Note right of Userorg: (code) src\Userorg.Adapter\Waldur\Orchestrators\WaldurExportOrchestrator.cs  — synchronizationChannel.Writer.WriteAsync(workItem)

  SyncQueue->>Consumer: Dequeue WorkItem(ProjectShortName, ResourceName)
  Consumer->>Consumer: Validate & Deduplicate (WorkItem.Key)
  Consumer->>Userorg: Resolve IProjectCommandTemplateSyncService and Trigger Sync
  Note right of Consumer: (code) src\Modules\Integrations\Heappe\Features\BackgroundSync\ChannelWorker.cs
  Note right of Consumer: (code) src\Modules\Integrations\Heappe\Features\BackgroundSync\WorkItemProcessor.cs — _syncService.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, ct)

  Note over Userorg: End iteration (proceed to next project / next scheduled iteration)
```


Key endpoints (summary):
- Waldur (configured in BaseIntegrationOptions):
  - {ProjectsEndpoint}               (e.g. /api/projects/)
  - {MarketplaceResourcesEndpoint}   (e.g. /api/marketplace-resources/)
  - {MarketplacePublicOfferingsEndpoint} (e.g. /api/marketplace-public-offerings/)
  - {CustomersEndpoint}              (e.g. /api/customers/)
  - Project users: {ProjectsEndpoint}/{projectId}/list_users/

- HEAppE (methods exposed by HeappeService):
  - GET /heappe/Management/ProjectsByAccountingStrings?AccountingString={resourceName}
  - POST /heappe/Management/Project
  - PUT  /heappe/Management/Project
  - GET  /heappe/Management/ProjectAssignmentToClusters?ProjectId={projectId}
  - POST /heappe/Management/ProjectAssignmentToCluster
  - PUT  /heappe/Management/ProjectAssignmentToCluster
  - GET  /heappe/Management/Clusters

Code references (enqueue & consumer):
- src\Userorg.Adapter\Waldur\Orchestrators\WaldurExportOrchestrator.cs — enqueue after tx.CommitAsync: var workItem = new WorkItem(...); await synchronizationChannel.Writer.WriteAsync(workItem, ct); (≈ lines 143–146)
- src\Modules\Integrations\Heappe\Features\BackgroundSync\ChannelWorker.cs — consumer loop resolves IProjectCommandTemplateSyncService and constructs WorkItemProcessor (reader loop ≈ lines 66–72)
- src\Modules\Integrations\Heappe\Features\BackgroundSync\WorkItemProcessor.cs — calls await _syncService.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, ct); (≈ line 85)

Alternative branches shown in the diagrams:
- Waldur API does not respond (non-2xx): retry according to "waldur-retry" or write to DLQ
- Resource mapping invalid / missing data: resource skipped and recorded
- HEAppE mapping missing (no HEAppE configuration for provider): initialization skipped, error recorded
- HEAppE returns error during create/update/assignment: rollback transaction and retry/DLQ

Note: the specific names of configured endpoints are provided by the BaseIntegrationOptions implementation and set in configuration (appsettings). The diagram uses these symbolic names ({ProjectsEndpoint}, ...); in real deployments they will contain full relative paths.
