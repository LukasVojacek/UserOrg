using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RemI;
using RemI.Features.Projects;
using RemI.Features.Users;

using Userorg.Adapter.RemI.Userorg;

using IUserorgService = RemI.HostContracts.IUserorgService;

namespace Userorg.Adapter.RemI;

internal static class RemIAdapterExtensions
{
    // invariant: the route prefix and both paths are already mapped in roleManagmentRBAC.json against perm:service:remi, and the Gateway is fail-closed on a path it has no mapping for
    private const string ApiRoutePrefix = "api/remi";

    private const string ApiTagName = "RemI";

    internal static IHostApplicationBuilder AddRemIAdpIntegration(this IHostApplicationBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);

        hostBuilder.Services.AddScoped<IUserorgService, UserorgService>();
        hostBuilder.AddRemIIntegration();

        return hostBuilder;
    }

    internal static WebApplication MapRemIIntegrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutePrefix)
            .WithTags(ApiTagName)
            .RequireAuthorization();

        group.MapGetUsersEndpoint();
        group.MapGetProjectsEndpoint();
        group.MapGetSystemRolesEndpoint();

        return app;
    }
}