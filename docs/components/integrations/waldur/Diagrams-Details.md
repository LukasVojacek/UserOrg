# Waldur Integration — Diagram Details (Mermaid)

The following are refined mermaid diagrams reflecting the implementation from WaldurPlatform.Base.

1) Flowchart — overall sync pipeline

```mermaid
flowchart LR 
  Start --> Check(Is Waldur enabled?)
  Check -- Yes --> Register[AddWaldurPlatformIntegration]
  Register --> WaldurHttpClient
  Register --> HostedServices[Start DataConsumer and Processing Background Services]
  HostedServices --> Collector[CollectorService ICollectorService]
  Collector --> ProviderProducer[ProviderProducer]
  Collector --> ProjectProducer[ProjectProducer]
  ProviderProducer --> ProviderChannel[Channel ProviderDto]
  ProjectProducer --> ProjectChannel[Channel ProjectDto]
  UserProducer[from ProjectProducer enqueues UserDto] --> UserChannel[Channel UserDto]
  ProviderChannel --> ProviderConsumer[ProviderConsumer]
  ProjectChannel --> ProjectConsumer[ProjectConsumer]
  UserChannel --> UserConsumer[UserConsumer]
  ProviderConsumer --> AggregateChannel[Channel AggregateData]
  ProjectConsumer --> AggregateChannel
  UserConsumer --> AggregateChannel
  AggregateChannel --> DataConsumer[DataConsumerBackgroundService]
  DataConsumer --> Exporter[IExporter host implemented]
  Exporter --> DB[(Database)]
```

2) Activity — Drain & Phase progression

```mermaid
flowchart TD
  StartIteration --> PhaseProviders[Phase: Providers]
  PhaseProviders --> DrainProviders[Drain AggregateChannel]
  DrainProviders --> PhaseProjects[Phase: Projects]
  PhaseProjects --> DrainProjects[Drain AggregateChannel]
  DrainProjects --> PhaseUsers[Phase: Users]
  PhaseUsers --> DrainUsers[Drain AggregateChannel]
  DrainUsers --> EndIteration[End iteration]
```

Notes:
- Telemetry: SyncTelemetry exposes ActivitySource and Meter names used across the pipeline.
- Resilience: a named resilience handler ("waldur-retry") is applied to the HttpClient.
