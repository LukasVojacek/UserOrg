# Waldur Integration — Overview

This document summarizes the Waldur integration in the UserOrg project.

At a glance
- Integration name: Waldur
- Named HttpClient: Constants.WaldurHttpClientName ("WaldurHttpClient")
- Entry points:
  - ServiceCollectionExtensions.AddWaldurIntegration (startup)
  - Background periodic sync (ProcessingBackgroundService / PipelineOrchestrator)
- Key dependencies: WaldurPlatform.Base, Waldur.Contracts, UserOrg.Shared
- Configuration: WaldurOptions (configuration section)

What this documentation covers
- Architecture and flow (Flow-Waldur.md)
- Sequence (Sequence-Waldur.md)
- Models and host contracts (Models-and-Contracts.md)
- Repository and persistence (Repository-DataFlow.md)

Implementation files referenced
- src\Modules\Integrations\Waldur\ServiceCollectionExtensions.cs — integration registration and instrumentation
- src\Modules\Integrations\Waldur\Worker\BackgroundPeriodicSync* — background job entrypoints
- src\Modules\Integrations\Waldur\Configuration\WaldurOptions.cs — configuration options
- src\Modules\Integrations\Waldur\Repository\* — repository adapter (host-side)
