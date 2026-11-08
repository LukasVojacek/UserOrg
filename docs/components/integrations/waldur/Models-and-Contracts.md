# Waldur Models and Host Contracts

This document describes the models and host contracts used by the integration (extracted from WaldurPlatform.Base).

Where models are defined
- Main model definitions: src/Modules/Integrations/WaldurPlatform.Base/Features/DataCollector/WaldurModels.cs
- Host contracts: src/Modules/Integrations/WaldurPlatform.Base/HostContracts/ (IExporter) and the local HostContracts README in the Waldur module

Primary API models (summary)
- ApiCustomer
  - uuid (Id : Guid), name, display_name, description, country, created
  - Used to produce ProviderDto

- ApiProject
  - uuid (Id : Guid), name, slug, description, start_date, end_date, created, is_removed
  - Used to produce ProjectDto and UserDto

- ApiProjectUser
  - uuid, role_uuid (RoleId), role_name, user_uuid (UserId), user_username, user_email, created, created_by_uuid
  - Used while enriching UserDto (collector.GetProjectUsersAsync)

- ApiMarketplacePublicOffering / ApiMarketplacePublicOfferingDetail
  - uuid, name, description, endpoints, components, customer_uuid, created, state, slug, category_title, shared, plugin_options
  - Heappe-specific plugin options: heappe_local_base_path

- ApiMarketplaceResource
  - uuid, name, backend_id, effective_id, project_uuid (ProjectId), offering_uuid (OfferingId), offering_name, limits (allocations), state, end_date, created, modified
  - `effective_id` is collected alongside `backend_id`; downstream processing accepts either identifier, with `effective_id` preferred when both are present

Serialization notes
- GuidNoDashJsonConverter accepts GUIDs with or without dashes (supports "N" and "D" formats).
- JsonPropertyName attributes map fields to Waldur API JSON keys.

Host contracts (signatures)
- IExporter (host implementation — from WaldurPlatform.Base.HostContracts):
  - Task<Result<ValidationError>> ExportData(ProjectDto projectData, CancellationToken cancellationToken);
  - Task<Result<ValidationError>> ExportData(ProviderDto providerData, CancellationToken cancellationToken);
  - Task<Result<ValidationError>> ExportData(UserDto userData, CancellationToken cancellationToken);
  - Task<Result<Error>> InitializeSystemExportUser(CancellationToken cancellationToken);

- IWaldurIntegration (from Waldur.Contracts):
  - Task<Result<Error>> SynchronizeAsync();
  - This interface provides a simple entry point for host-triggered synchronization.

- WaldurConstants (from Waldur.Contracts):
  - ServiceName, ServiceAccountUserName, ServiceAccountEmail, ServiceProjectDomainName
  - Role names: WaldurProjectManagerRoleName, WaldurProjectMemberRoleName
  - AllocationUnit mapping: UserorgWaldurAllocationUnitMap (see AllocationUnitMapping record)

- ICollectorService (CollectorService provides these methods):
  - IAsyncEnumerable<ApiCustomer> GetCustomersAsync(CancellationToken ct = default);
  - IAsyncEnumerable<ApiProject> GetProjectsAsync(CancellationToken ct = default);
  - IAsyncEnumerable<ApiProjectUser> GetProjectUsersAsync(Guid projectId, CancellationToken ct);
  - IAsyncEnumerable<ApiMarketplaceResource> GetMarketplaceResourcesByProjectAsync(Guid projectId, CancellationToken ct);
  - IAsyncEnumerable<ApiMarketplacePublicOffering> GetMarketplaceOfferingsByCustomerAsync(Guid customerId, CancellationToken ct);
  - Task<ApiMarketplacePublicOffering?> GetMarketplacePublicOfferingAsync(Guid offeringUuid, CancellationToken ct);

Local DTOs and AggregateData
- ProjectDto, ProviderDto, UserDto are wrapper DTOs defined in the host contracts in WaldurPlatform.Base and contain:
  - the source Api* model and fields for enriched collections (for example, Users in UserDto)
- AggregateData is the type sent to a central Channel<AggregateData> consumed by the export service.

Code references
- src/Modules/Integrations/WaldurPlatform.Base/Features/DataCollector/WaldurModels.cs
- src/Modules/Integrations/WaldurPlatform.Base/HostContracts/IExporter.cs
- src/Modules/Integrations/Waldur/Configuration/WaldurOptions.cs
