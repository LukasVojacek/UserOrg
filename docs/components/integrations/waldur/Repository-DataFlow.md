# Waldur Repository — Data Flow

This section describes how data flows from the Waldur API to the host persistence layer (IExporter / repository).

Component roles
- Producer (ProviderProducer / ProjectProducer): calls ICollectorService and produces ProviderDto / ProjectDto / UserDto
- Consumer (ProviderConsumer / ProjectConsumer / UserConsumer): enriches DTOs (e.g., fetch project users), performs validation and writes AggregateData to the central channel
- DataConsumerBackgroundService: reads AggregateData from the channel and calls IExporter.ExportData(...) for persistence
- IExporter (host): implements business logic to create/update local entities

Detailed flow
1. Producers enumerate the API (GetCustomersAsync / GetProjectsAsync) and write DTOs into their respective channels
2. Consumers read from channels, enrich data with collector calls (e.g., GetProjectUsersAsync), validate and write AggregateData into the central channel
3. DataConsumer background service reads AggregateData and for each record calls the corresponding IExporter.ExportData(ProjectDto | ProviderDto | UserDto)
4. IExporter performs transactional upsert into the database (host implementation), returning Result with possible ValidationError or Error
5. DataConsumer records success/failure in telemetry; on failure the record may be placed into the DLQ

Error handling
- Validation errors: IExporter returns Result<ValidationError> — these are logged and often routed to the DLQ
- Transient errors (network, timeouts): handled by CollectorService via retry/resilience; exporter can apply retries or route to DLQ

Expected host/ repository signatures
- Task<Result<ValidationError>> IExporter.ExportData(ProjectDto projectData, CancellationToken cancellationToken)
- Task<Result<ValidationError>> IExporter.ExportData(ProviderDto providerData, CancellationToken cancellationToken)
- Task<Result<ValidationError>> IExporter.ExportData(UserDto userData, CancellationToken cancellationToken)
