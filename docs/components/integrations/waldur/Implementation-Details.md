# Waldur Integration — Implementation Details

This document describes the current implementation of the Waldur integration module in the repository.

1) Project and references
- Project file: src/Modules/Integrations/Waldur/Waldur.csproj
- Referenced projects:
  - ..\..\..\UserOrg.Shared\UserOrg.Shared.csproj
  - ..\WaldurPlatform.Base\WaldurPlatform.Base.csproj
  - ..\Waldur.Contracts\Waldur.Contracts.csproj
- InternalsVisibleTo: Waldur.Tests (test assembly)

2) Configuration
- src/Modules/Integrations/Waldur/Configuration/WaldurOptions.cs
  - SectionName = "Waldur"
  - Properties: Enabled (bool), HeappeInstancesMap (map of identifier -> URI)
  - Helper: WaldurConfigurationExtensions.IsWaldurEnabled reads configuration keys "Waldur:Enabled" or "Waldur__Enabled" and accepts boolean values.

3) Service registration (startup)
- Entry point: ServiceCollectionExtensions.AddWaldurIntegration(IHostApplicationBuilder, string)
  - Checks configuration via IsWaldurEnabled
  - If enabled: binds IOptions<WaldurOptions> and calls builder.AddWaldurPlatformIntegration<WaldurOptions>(WaldurDescriptor, sectionName)
  - WaldurDescriptor includes: IntegrationName = "Waldur", HttpClientName = Constants.WaldurHttpClient, ResilienceHandlerName = "waldur-retry"
- Instrumentation helpers exposed in the module:
  - AddWaldurSyncInstrumentation(TracerProviderBuilder, SyncTelemetry)
  - AddWaldurSyncInstrumentation(TracerProviderBuilder)
  - AddWaldurSyncMetrics(MeterProviderBuilder, SyncTelemetry)
  - AddWaldurSyncMetrics(MeterProviderBuilder)

4) Constants
- Constants.WaldurHttpClientName = "WaldurHttpClient"

5) Worker/Features/Repository/Validators
- These directories are present and act as extension points, but most runtime behavior is implemented in WaldurPlatform.Base.
- Observed pattern: the module registers and composes platform features (collector, pipeline, telemetry) while concrete export logic lives in host implementations.

6) Where the sync logic is implemented
- AddWaldurPlatformIntegration<TOptions> (in WaldurPlatform.Base) wires:
  - Named HttpClient and RateLimitHandler
  - Resilience handler configuration ("waldur-retry")
  - Channel and DLQ setup, telemetry, and hosted background services
  - CollectorService (ICollectorService) for paged API fetching
  - Pipeline orchestration (producers/consumers)

Therefore, this module serves primarily as a thin wiring layer that:
- exposes configuration options
- registers the platform integration with well-known names
- provides instrumentation helpers
- exposes local extension points for host implementations (IExporter, repository adapters)

7) Documentation implications
- To fully document runtime details (exact DTO fields, mapping logic, exporter signatures), examine:
  - src/Modules/Integrations/WaldurPlatform.Base
  - src/Modules/Integrations/Waldur.Contracts