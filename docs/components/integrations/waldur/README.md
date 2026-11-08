# Waldur Integration

Integration with Waldur platform for data collection, background synchronization, and data export. Enables bi-directional sync of providers, projects, and users.

## Overview

The Waldur integration provides:
- **Data collection** — REST API client for Waldur data
- **Background periodic sync** — Producer/consumer pipeline
- **Data import** — Import Waldur data into UserOrg
- **Data export** — Export UserOrg data back to Waldur
- **HEAppE initialization** — Set up HEAppE projects in Waldur

**Location:** `src/Modules/Integrations/Waldur/` and `src/Modules/Integrations/WaldurPlatform.Base/`

## Key Components

### Data Collection
- `CollectorService` — Fetch data from Waldur REST API
- `WaldurModels` — Data models for providers, projects, users
- `WaldurModelsExtensions` — Model transformations

**See:** [Data Collection Guide](data-collection.md)

### Background Periodic Sync Pipeline
Main orchestrator for continuous data synchronization:

- `ProcessingBackgroundService` — Starts sync pipeline on schedule
- `PipelineOrchestrator` — Coordinates producers and consumers
- `PipelineDepthState` — Track pipeline execution state

**Producers** (fetch data from Waldur):
- `ProviderProducer` — Fetch provider data
- `ProjectProducer` — Fetch project data
- `UserProducer` — Fetch user data (via ProjectProducer)

**Consumers** (process and import data):
- `ProjectConsumer` — Process projects, update database
- `UserConsumer` — Process users, update database
- `ProviderConsumer` — Process providers, update database
- `DataConsumerBackgroundService` — Aggregates and imports data

**State Management:**
- `SyncProcessingState` — Track current sync state
- `ProcessedItemRegistry` — Remember processed items
- `SyncDeadLetterQueue` — Handle failed items
- `ProjectSyncSkipRegistry` — Skip certain projects
- `SyncIterationDbChangeCounter` — Track changes per iteration

**See:** [Background Periodic Sync Guide](background-periodic-sync.md)

### Data Validation
- `AggregateDataValidator` — Validate imported data
- `AggregateData` — Aggregated data structure
- `AggregateDataExtensions` — Validation helpers

### Queue Processing
- `StompQueueBackgroundService` — Process messages from STOMP queue
- Message parsing and routing

### Data Export
- `WaldurExporter` — Export UserOrg data to Waldur
- `UserAssignmentEntities` — User assignment export format

**See:** [Data Export Guide](export.md)

### Orchestrators
- `WaldurExportOrchestrator` — Coordinate export operations
- `WaldurHeappeProjectInitializer` — Initialize HEAppE projects in Waldur

### Observability
- `SyncTelemetry` — Sync operation metrics
- `SyncTraceContextExtensions` — Distributed trace context
- Logging at each pipeline stage

### Configuration
- `WaldurOptions` — Configuration settings (API URL, credentials)
- `RateLimitHandler` — API rate limiting

### Contracts
- Data transfer objects (DTOs)
- `SyncIterationDbChangesSnapshot` — Capture database changes
- Project, Provider, User DTOs

## Complete Guides

### [Data Collection Guide](data-collection.md)
REST API client and data model reference.

**Covers:**
- Waldur API authentication
- Available endpoints
- Data model transformations
- Error handling

### [Background Periodic Sync](background-periodic-sync.md)
Producer/consumer pipeline for continuous synchronization.

**Covers:**
- Pipeline architecture
- Producer/consumer pattern
- State management
- Sync scheduling
- Performance considerations

### [Data Export Guide](export.md)
Export UserOrg data back to Waldur.

**Covers:**
- Export orchestration
- Supported data types
- Format and validation
- Conflict resolution

## Detailed Documentation

For deep dives into specific areas:

- [Implementation Details](Implementation-Details.md) — Architecture patterns, design decisions
- [Models and Contracts](Models-and-Contracts.md) — DTOs and data structures
- [Data Flow](Repository-DataFlow.md) — How data flows through the system
- [Diagrams](Diagrams-Details.md) — Visual system architecture
- [Sequence Diagrams](Sequence-Userorg-Waldur-HEAppE.md) — Interaction sequences
- [Platform Base Details](PlatformBase-Details.md) — Shared infrastructure

## Diagrams

Visual representations:
- `userorg_waldur_sync.excalidraw` — Sync pipeline architecture

## Typical Sync Flow

1. **Collection** — Producers fetch data from Waldur API
2. **Aggregation** — Data is aggregated into work items
3. **Consumption** — Consumers process and validate data
4. **Import** — Data imported into UserOrg database
5. **Export** — UserOrg changes exported back to Waldur (async)

## Integration with Other Components

- **Core API** — Provider, project, user context
- **HEAppE** — Template and cluster information
- **Gateway** — Authentication and authorization
- **Infrastructure** — Database persistence

## Status

✅ Fully implemented and production-ready

- Complete data collection pipeline
- Bi-directional synchronization
- Background workers for async operations
- Comprehensive error handling and recovery
- State management for reliable sync
- STOMP queue support
