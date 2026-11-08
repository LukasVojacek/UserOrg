using HashiCorpVault;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

using Userorg.Adapter.Email;
using Userorg.Adapter.Heappe;
using Userorg.Adapter.RemI;

using UserOrg.Ddi.Composition;

namespace Userorg.Adapter;

public static class UserorgAdapterExtensions
{
    /// <summary>
    /// Adds the necessary modules and integrations to the host application builder for the Userorg Adapter. This includes
    /// the Heappe module and HashiCorp Vault integration.
    /// </summary>
    /// <param name="hostBuilder">The host application builder to which the modules and integrations will be added.</param>
    public static void AddModules(this IHostApplicationBuilder hostBuilder)
    {
        hostBuilder.AddHeappeIntegration();

        hostBuilder.AddEmailSenderAdapter();

        hostBuilder.AddHashiCorpVaultIntegration();

        hostBuilder.AddDdi();

        hostBuilder.AddRemIAdpIntegration();
    }

    /// <summary>
    /// Configures and maps all module endpoints to the specified web application.
    /// </summary>
    /// <param name="app">The web application to which the module endpoints will be mapped.</param>
    public static void MapModules(this WebApplication app)
    {
        app.MapHeappeIntegrationEndpoints();

        app.MapDdiEndpoints();

        app.MapRemIIntegrationEndpoints();
    }
}