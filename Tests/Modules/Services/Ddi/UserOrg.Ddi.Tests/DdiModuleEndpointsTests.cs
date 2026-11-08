using Core.DataAccess.Queries.Contracts;

using Forge.Core.Features.Role.Services;

using HashiCorpVault.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using UserOrg.Ddi.Composition;

using Xunit;

namespace UserOrg.Ddi.Tests;

public sealed class DdiModuleEndpointsTests
{
    [Fact]
    public void Should_Map_DataStaging_And_Service_Groups_When_MapDdiEndpoints_Called()
    {
        var routes = MappedRoutes();

        Assert.Contains(routes, r => r.StartsWith("api/v2/DataStaging", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(routes, r => r.StartsWith("api/v2/Service", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_Require_Authorization_On_Ddi_Groups_When_Mapped()
    {
        var ddiEndpoints = DdiEndpoints();

        Assert.NotEmpty(ddiEndpoints);
        Assert.All(ddiEndpoints, e => Assert.Contains(e.Metadata, m => m is IAuthorizeData));
    }

    private static List<RouteEndpoint> DdiEndpoints() =>
        MappedRouteEndpoints()
            .Where(e => IsDdiRoute(e.RoutePattern.RawText))
            .ToList();

    private static List<string> MappedRoutes() =>
        MappedRouteEndpoints()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToList();

    // Read the endpoints straight off the app's route-builder data sources: the DI-resolved
    // composite EndpointDataSource is only populated once the request pipeline is built (on Run).
    private static List<RouteEndpoint> MappedRouteEndpoints()
    {
        var builder = WebApplication.CreateSlimBuilder();
        // The real handlers take service dependencies and require authorization; the test host must
        // register them so the grouped endpoints can be built when their data source is enumerated.
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Mock.Of<IHashiCorpVaultClient>());
        builder.Services.AddSingleton(Mock.Of<IDdiProjectsInfoQuery>());
        builder.Services.AddSingleton(Mock.Of<ISyncDetailsQuery>());
        builder.Services.AddSingleton(Mock.Of<IRoleListService>());
        using var app = builder.Build();
        app.MapDdiEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static bool IsDdiRoute(string? rawText) =>
        rawText is not null
        && (rawText.StartsWith("api/v2/DataStaging", StringComparison.OrdinalIgnoreCase)
            || rawText.StartsWith("api/v2/Service", StringComparison.OrdinalIgnoreCase));
}
