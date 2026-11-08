using Core.DataAccess.Queries.Contracts;
using Core.Features.Enums.GetProjectDomainTypes;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.Enums;

public sealed class GetProjectDomainTypesEndpointTests
{
    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProjectDomainTypes";
        return http;
    }

    private static async Task<(IResult Result, DefaultHttpContext Http)> InvokeAsync(List<DomainTypeName> names)
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectDomainTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);

        var http = HttpContext();
        var result = await GetProjectDomainTypesEndpoint.HandleAsync(http, query.Object, CancellationToken.None);
        return (result, http);
    }

    [Fact]
    public async Task Should_ReturnEmptyCompletePage_When_NoDomainTypes()
    {
        var (result, http) = await InvokeAsync([]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectDomainTypesEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }

    [Fact]
    public async Task Should_MapNamesToResponse_AsCompletePage_When_DomainTypesExist()
    {
        var (result, http) = await InvokeAsync([DomainTypeName.Create("HPC"), DomainTypeName.Create("Cloud")]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectDomainTypesEndpoint.Response>>>(result);
        Assert.Equal(["HPC", "Cloud"], ok.Value!.Select(r => r.Name));
        Assert.Equal("2", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }
}
