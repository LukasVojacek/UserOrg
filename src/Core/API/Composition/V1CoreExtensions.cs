using FluentValidation;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.FeatureManagement;
using UserOrg.Shared.OpenApi;

using UserOrganizationManagement.API.API;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.OpenApi;
using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.ExtModels;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess;
using UserOrganizationManagement.Infrastructure.DataAccess.Factory;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

namespace UserOrganizationManagement.API.Composition;

internal static class V1CoreExtensions
{
    internal static WebApplicationBuilder AddV1Core(this WebApplicationBuilder builder, bool isV1ApiEnabled = true)
    {
        builder.Services.AddUserOrgDataAccess(
            builder.Configuration.GetConnectionString("UserOrgDBContext"),
            builder.Environment.IsDevelopment(),
            builder.Environment.IsDevelopment());

        builder.Services.AddScoped<IAllocationUnitTypeRepository, AllocationUnitTypeRepository>();
        builder.Services.AddScoped<ISystemModuleRepository, SystemModuleRepository>();
        builder.Services.AddScoped<IAssignedLocationRepository, AssignedLocationRepository>();
        builder.Services.AddScoped<IAssignedLocationValueRepository, AssignedLocationValueRepository>();
        builder.Services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        builder.Services.AddScoped<IConfigurationTypeRepository, ConfigurationTypeRepository>();
        builder.Services.AddScoped<ICountryRepository, CountryRepository>();
        builder.Services.AddScoped<IProjectDomainTypeRepository, ProjectDomainTypeRepository>();
        builder.Services.AddScoped<IRequestRepository, RequestRepository>();
        builder.Services.AddScoped<IProjectTagRepository, ProjectTagRepository>();
        builder.Services.AddScoped<IUserPermissionRepository, UserPermissionRepository>();
        builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
        builder.Services.AddScoped<IProjectResourceRepository, ProjectResourceRepository>();
        builder.Services.AddScoped<IUserProjectAccessRepository, UserProjectAccessRepository>();
        builder.Services.AddScoped<IProviderRepository, ProviderRepository>();
        builder.Services.AddScoped<ILocationRepository, LocationRepository>();
        builder.Services.AddScoped<ILocationTypeRepository, LocationTypeRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();

        builder.Services.AddScoped<IActionLinkRepository, ActionLinkRepository>();

        builder.Services.AddScoped<IUserOrgUnitOfWork, UserOrgUnitOfWork>();
        builder.Services.AddScoped<IProjectService, ProjectService>();
        builder.Services.AddScoped<IProjectResourceService, ProjectResourceService>();
        builder.Services.AddScoped<IProjectResourceAssignmentService, ProjectResourceAssignmentService>();
        builder.Services.AddScoped<IProjectTagService, ProjectTagService>();
        builder.Services.AddScoped<IProviderService, ProviderService>();
        builder.Services.AddScoped<ILocationService, LocationService>();
        builder.Services.AddScoped<IOtherService, OtherService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IAdministrationService, AdministrationService>();
        builder.Services.AddScoped<IHeappeIntegrationService, HeappeIntegrationService>();

        builder.Services.AddScoped<IActionLinkService, ActionLinkService>();
        builder.Services.AddScoped<IProjectRequestService, ProjectRequestService>();

        // Register ProviderIdResolver as singleton — caches provider ID lookups with 60 s TTL
        // to avoid a DB round-trip in JWT OnTokenValidated for every ProviderAdministrator request.
        builder.Services.AddSingleton<IProviderIdResolver, ProviderIdResolver>();

        // Legacy specs preserved verbatim (path-substring split); kept apart from the v2 specs by the "api/v2/" route prefix.
        if (isV1ApiEnabled)
        {
            builder.Services.AddOpenApi(ApiDocuments.User, ConfigureLegacyOpenApi);
            builder.Services.AddOpenApi(ApiDocuments.Administration, ConfigureLegacyOpenApi);
        }

        builder.Services.AddOpenApi(ApiDocuments.Service, ConfigureServiceOpenApi);

        builder.Services.AddValidatorsFromAssemblyContaining<IAssemblyMarker>(ServiceLifetime.Singleton);

        return builder;
    }

    /// <summary>
    /// Maps the legacy (v1) Core endpoint groups and marks each with the <see cref="FeatureFlags.V1Api"/>
    /// runtime gate. The caller decides whether to map them at all; this method only wires the per-request
    /// kill-switch enforced by <c>FeatureFlagMiddleware</c>.
    /// </summary>
    /// <remarks>
    /// <c>api/version</c> and <c>api/DataStaging</c> are mapped by <c>Program.cs</c> outside this method and
    /// stay ungated: the first is an ops endpoint, the second serves DDI service tokens rather than the v1 UI.
    /// </remarks>
    internal static WebApplication MapV1CoreEndpoints(this WebApplication app)
    {
        app.RegisterApiRoutes().EnabledWhen(FeatureFlags.V1Api);
        app.RegisterServiceEndpoint().EnabledWhen(FeatureFlags.V1Api);
        app.RegisterPublicEndpoint().EnabledWhen(FeatureFlags.V1Api);
        return app;
    }

    // Legacy documents (User, Administration): the original path-substring split, restricted to the
    // pre-v2 endpoints so refactored routes never leak into the preserved specs.
    private static void ConfigureLegacyOpenApi(Microsoft.AspNetCore.OpenApi.OpenApiOptions options)
    {
        options.ShouldInclude = api => !V2CoreExtensions.IsV2(api) && IsV1Gated(api);
        HostInfrastructureExtensions.ConfigureSharedOpenApi(options);
        // Collections of the legacy Domain.ValueObjects value objects (UserModel.InProjects) reach the document
        // without an `items` schema, because System.Text.Json's schema exporter cannot describe a type behind a
        // custom JsonConverter and omits the element schema entirely. Only the legacy documents use those types
        // (the v2 stack has its own Core.Domain.Project.ProjectShortName), so the repair is registered here
        // rather than in ConfigureSharedOpenApi.
        options.AddSchemaTransformer<ValueObjectCollectionSchemaTransformer>();
        options.AddDocumentTransformer<DocumentPathGroupFilter>();
    }

    private static void ConfigureServiceOpenApi(Microsoft.AspNetCore.OpenApi.OpenApiOptions options)
    {
        options.ShouldInclude = api => !V2CoreExtensions.IsV2(api) && !IsV1Gated(api);
        HostInfrastructureExtensions.ConfigureSharedOpenApi(options);
        options.AddSchemaTransformer<ValueObjectCollectionSchemaTransformer>();
        options.AddDocumentTransformer<UnversionedHeappeTagDocumentTransformer>();
    }

    private static bool IsV1Gated(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription api) =>
        api.ActionDescriptor.EndpointMetadata.OfType<EnabledByFeatureFlagMetadata>().Any(m => m.FeatureFlagName == FeatureFlags.V1Api);
}