# WaldurPlatform.Base — Extracted Implementation Details

This document summarizes key components from src/Modules/Integrations/WaldurPlatform.Base that implement the core synchronization pipeline.

1) IntegrationDescriptor
- Type: IntegrationDescriptor(IntegrationName, HttpClientName, ResilienceHandlerName)
- Centralizes naming for the integration (HttpClient and resilience handler)

2) ServiceCollectionExtensions.AddWaldurPlatformIntegration
- Registers:
  - Named HttpClient with User-Agent, BaseAddress, authorization header and RateLimitHandler
  - Resilience handler with retry policy (exponential backoff + jitter)
  - Singletons: IntegrationDescriptor, SyncTelemetry
  - Scoped CollectorService<TOptions> (ICollectorService)
  - Channel<AggregateData> (unbounded singleton), DLQ, processing state, registries
  - Hosted services: DataConsumerBackgroundService<TOptions>, ProcessingBackgroundService<TOptions>

3) CollectorService
- ICollectorService implementation that fetches paged results from Waldur API using named HttpClient
- Exposes methods: GetCustomersAsync, GetProjectsAsync, GetProjectUsersAsync, GetMarketplaceResourcesByProjectAsync, GetMarketplaceOfferingsByCustomerAsync, GetMarketplacePublicOfferingAsync
- Implements generic paged request helper GetPagedApiResultsAsync<T>
- Uses JsonSerializer and custom GuidNoDashJsonConverter

4) Pipeline orchestration
- ProcessingBackgroundService runs iterations with PipelineOrchestrator
- PipelineOrchestrator initializes channels and producers/consumers for providers, projects, users
- Producer classes (ProviderProducer, ProjectProducer) call collector to enumerate API results and post DTOs into channels
- Consumer classes (ProviderConsumer, ProjectConsumer, UserConsumer) enrich items (e.g., fetch project users) and write AggregateData into the aggregate channel for export
- IExporter (HostContracts) defines ExportData(ProjectDto/ProviderDto/UserDto) method signatures that host implements to persist imported data

5) Models
- Waldur models (ApiCustomer, ApiProject, ApiProjectUser, ApiMarketplaceResource, ApiMarketplacePublicOffering, etc.) defined in WaldurPlatform.Base.Features.DataCollector.WaldurModels
- They include JsonPropertyName attributes matching Waldur API fields

6) Telemetry
- SyncTelemetry provides ActivitySource/Meter names and metrics used across pipeline for tracing and metrics

7) Error handling
- DLQ (SyncDeadLetterQueue) used for validation failures
- Retry/resilience for HttpClient via resilience handler
- Per-item activities record success/failure and timing
