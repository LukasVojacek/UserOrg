using UserOrg.Gateway.Configuration;

namespace UserOrg.Gateway.Tests;

public sealed class PublicEndpointsOptionsTests
{
    [Fact]
    public void ConstructorDefaultPathsIncludeScalarAssetsAndFavicon()
    {
        var options = new PublicEndpointsOptions();

        Assert.Contains(options.Paths, path => path.Pattern == "/api" && path.MatchType == PathMatchType.Exact);
        Assert.Contains(options.Paths, path => path.Pattern == "/api/scalar.js" && path.MatchType == PathMatchType.Exact);
        Assert.Contains(options.Paths, path => path.Pattern == "/api/scalar.aspnetcore.js" && path.MatchType == PathMatchType.Exact);
        Assert.Contains(options.Paths, path => path.Pattern == "/favicon.ico" && path.MatchType == PathMatchType.Exact);
    }

    [Fact]
    public void Should_IncludeApiPublicWithPrefixMatch_When_DefaultPathsRequested()
    {
        var options = new PublicEndpointsOptions();

        Assert.Contains(options.Paths, path => path.Pattern == "/api/Public" && path.MatchType == PathMatchType.Prefix);
    }

    [Fact]
    public void Should_IncludeV2PublicWithPrefixMatch_When_DefaultPathsRequested()
    {
        var options = new PublicEndpointsOptions();

        Assert.Contains(options.Paths, path => path.Pattern == "/api/v2/Public" && path.MatchType == PathMatchType.Prefix);
    }

    [Fact]
    public void Should_IncludeHealthWithPrefixMatch_When_DefaultPathsRequested()
    {
        var options = new PublicEndpointsOptions();

        Assert.Contains(options.Paths, path => path.Pattern == "/health" && path.MatchType == PathMatchType.Prefix);
    }

    [Fact]
    public void Should_IncludeAllRequiredExactEntries_When_DefaultPathsRequested()
    {
        var options = new PublicEndpointsOptions();

        string[] requiredExact =
        [
            "/api",
            "/api/scalar.js",
            "/api/scalar.aspnetcore.js",
            "/api/User/openapi.json",
            "/api/Administration/openapi.json",
            "/api/Service/openapi.json",
            "/api/v2-User/openapi.json",
            "/api/v2-ProviderAdmin/openapi.json",
            "/api/v2-Admin/openapi.json",
            "/favicon.ico",
            "/alive",
            "/api/version",
            "/api/v2/Version"
        ];

        foreach (var pattern in requiredExact)
        {
            Assert.Contains(options.Paths, path => path.Pattern == pattern && path.MatchType == PathMatchType.Exact);
        }
    }

    [Fact]
    public void Should_IncludeV2VersionOnlyAsExactMatch_When_DefaultPathsRequested()
    {
        var options = new PublicEndpointsOptions();

        var entry = Assert.Single(options.Paths, path => path.Pattern == "/api/v2/Version");
        Assert.Equal(PathMatchType.Exact, entry.MatchType);
    }

    [Fact]
    public void Should_ContainAtLeastTwelvePaths_When_DefaultPathsRequested()
    {
        var options = new PublicEndpointsOptions();

        Assert.True(options.Paths.Count >= 12,
            $"Expected at least 12 default paths but found {options.Paths.Count}. Required entries may have been accidentally removed.");
    }
}
