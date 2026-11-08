using Core.Features.Provider.ResourceAssignment;

using Heappe;
using Heappe.Features.CommandTemplateManagement.Deactivate;
using Heappe.Features.CommandTemplateManagement.GetAll;
using Heappe.Features.CommandTemplatePermissions;
using Heappe.Features.CommandTemplateSynchronization;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.Credentials;
using Heappe.Features.GetLocationPartitions;
using Heappe.Features.KillSwitch;
using Heappe.Features.LogRetrieval;
using Heappe.Features.ResourceValidityAudit;
using Heappe.HostContracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Userorg.Adapter.Heappe.Userorg;

using UserOrganizationManagement.Application.UserOrg.IServices;

using HeappeContracts = Heappe.Contracts;

namespace Userorg.Adapter.Heappe;

internal static class HeappeAdapterExtensions
{
    private const string _apiRoutePrefix = "api/Heappe";
    private const string _apiTagName = "Heappe";

    internal static IHostApplicationBuilder AddHeappeIntegration(this IHostApplicationBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);

        hostBuilder.Services.AddScoped<ICredentialsService, HeappeCredentialsService>();
        hostBuilder.Services.AddScoped<IUserorgService, UserorgService>();
        hostBuilder.Services.AddScoped<IHeappeAuditTargetProvider, HeappeAuditTargetProvider>();

        // Must stay a separate implementation from UserorgService: see IHeappeVersionSource.
        hostBuilder.Services.AddScoped<IHeappeVersionSource, HeappeVersionSource>();
        hostBuilder.Services.AddScoped<IHeappeConnectionService, HeappeConnectionService>();
        hostBuilder.AddHEAppEIntegration();
        hostBuilder.Services.AddScoped<IHeappeSynchronizationService, HeappeSynchronizationService>();
        hostBuilder.Services.AddScoped<IResourceSyncRunner, HeappeResourceSyncRunner>();
        hostBuilder.Services.AddSingleton<IClusterInfoCacheWarmer, ClusterInfoCacheWarmer>();
        hostBuilder.Services.AddScoped<HeappeContracts.IHeappeSynchronizationPort, HeappeSynchronizationPort>();

        return hostBuilder;
    }

    internal static WebApplication MapHeappeIntegrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(_apiRoutePrefix)
            .WithTags(_apiTagName)
            .RequireAuthorization();

        group.MapTemplatePermissionsEndpoint();
        group.MapCommandTemplateEndpoint();
        group.MapCommandTemplateActivateEndpoint();
        group.MapCommandTemplateDeactivateEndpoint();
        group.MapCommandTemplateSyncEndpoint();

        group.MapGetPartitionsEndpoint();
        group.MapPostTemplateEndpoint();
        group.MapDownloadChangedFilesEndpoint();

        group.MapRunResourceAuditEndpoint();
        group.MapKillSwitchEndpoints();

        return app;
    }
}