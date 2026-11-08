using Core.DataAccess.Queries.Contracts;
using Core.Features.Country.GetCountries;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.Other;

public sealed class GetCountriesEndpointTests
{
    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/Countries";
        return http;
    }

    [Fact]
    public async Task Should_MapSummariesToResponse_And_PassFilterThrough()
    {
        var query = new Mock<ICountryQuery>();
        query.Setup(q => q.GetCountriesAsync("CZ", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CountrySummary("CZ", "Czechia"), new CountrySummary("SK", null)]);

        var result = await GetCountriesEndpoint.HandleAsync(HttpContext(), "CZ", query.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetCountriesEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("CZ", ok.Value[0].Code);
        Assert.Equal("Czechia", ok.Value[0].Name);
        Assert.Null(ok.Value[1].Name);
        query.Verify(q => q.GetCountriesAsync("CZ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_SetTotalCountHeader_When_ReferenceList()
    {
        var http = HttpContext();
        var query = new Mock<ICountryQuery>();
        query.Setup(q => q.GetCountriesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CountrySummary("CZ", "Czechia"), new CountrySummary("SK", "Slovakia")]);

        var result = await GetCountriesEndpoint.HandleAsync(http, null, query.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetCountriesEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("2", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }
}
