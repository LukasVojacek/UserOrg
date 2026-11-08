using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

using UserOrg.Ddi.Features.DataStaging;
using UserOrg.Ddi.Features.Service;

namespace UserOrg.Ddi.Composition;

/// <summary>
/// Composition seam for the DDI (data-staging) service module. The module is a facing/composition
/// surface over the Core read side (<c>Core.DataAccess</c>) and the HashiCorp Vault client,
/// authenticated by the provider API key (service-to-service). See ADR-0017.
/// </summary>
public static class DdiModuleExtensions
{
    private const string DataStagingPrefix = "api/v2/DataStaging";
    private const string ServicePrefix = "api/v2/Service";
    private const string DataStagingTag = "DataStaging";
    private const string ServiceTag = "Service";

    /// <summary>
    /// Registers the DDI module's services on the host.
    /// </summary>
    /// <param name="builder">The host application builder used as the composition root.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddDdi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // ponytail: no module-owned services. DataStaging (block D2) uses the host's IHashiCorpVaultClient;
        // the Service endpoints use Core.DataAccess read queries (block D3) and — for SyncDetails (block D5) —
        // the host's Forge.Core IRoleListService (registered by AddForgeCore in Program.cs). This seam exists
        // so that any future module-owned wiring lands here, not in Program.cs.
        return builder;
    }

    /// <summary>
    /// Maps the DDI endpoint groups onto the host under the <c>api/v2/</c> route prefix
    /// (<c>api/v2/DataStaging/*</c>, <c>api/v2/Service/*</c>) — the discriminator that places them in the
    /// v2 OpenAPI documents and keeps them off the legacy <c>api/DataStaging</c> surface (still served by
    /// the v1 endpoints for existing DDI workers).
    /// </summary>
    /// <param name="app">The web application to map the DDI endpoints onto.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static WebApplication MapDdiEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var dataStaging = app.MapGroup(DataStagingPrefix)
            .WithTags(DataStagingTag)
            .RequireAuthorization();

        var service = app.MapGroup(ServicePrefix)
            .WithTags(ServiceTag)
            .RequireAuthorization();

        dataStaging.MapGetWorkerDataEndpoint()
            .MapGetDataStagingLocationsEndpoint()
            .MapUpdateDataStagingLocationsEndpoint();

        service.MapGetProjectsInfoEndpoint()
            .MapGetSyncDetailsEndpoint();

        return app;
    }
}