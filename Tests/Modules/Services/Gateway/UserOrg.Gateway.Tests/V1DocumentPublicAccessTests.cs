using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Configuration;
using UserOrg.Gateway.Startup;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// With <c>FeatureManagement:V1Api</c> off the gateway stops treating the legacy document URLs as public:
/// an anonymous request is no longer handed to the public scheme and so answers 401, while the always
/// published <c>Service</c> document stays public. The default paths are the gateway's own registration,
/// fed the flag through configuration as the host receives it.
/// </summary>
public sealed class V1DocumentPublicAccessTests
{
    private const string UserDocument = "/api/User/openapi.json";

    private const string AdministrationDocument = "/api/Administration/openapi.json";

    private const string ServiceDocument = "/api/Service/openapi.json";

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Should_ListTheLegacyDocumentsAsPublic_OnlyWhile_V1ApiIsOn(string? v1ApiFlag, bool expectedPublic)
    {
        using var provider = BuildServices(v1ApiFlag);

        var paths = provider.GetRequiredService<IOptions<PublicEndpointsOptions>>().Value.Paths;

        Assert.Equal(expectedPublic, paths.Any(path => path.Pattern == UserDocument && path.MatchType == PathMatchType.Exact));
        Assert.Equal(expectedPublic, paths.Any(path => path.Pattern == AdministrationDocument && path.MatchType == PathMatchType.Exact));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("false")]
    public void Should_ListTheServiceDocumentAsPublic_Whatever_V1ApiSays(string? v1ApiFlag)
    {
        using var provider = BuildServices(v1ApiFlag);

        var paths = provider.GetRequiredService<IOptions<PublicEndpointsOptions>>().Value.Paths;

        Assert.Contains(paths, path => path.Pattern == ServiceDocument && path.MatchType == PathMatchType.Exact);
    }

    [Theory]
    [InlineData("true", UserDocument, Constants.PublicWithInternalTokenSchemeId)]
    [InlineData("true", AdministrationDocument, Constants.PublicWithInternalTokenSchemeId)]
    [InlineData("true", ServiceDocument, Constants.PublicWithInternalTokenSchemeId)]
    [InlineData("false", UserDocument, Constants.BearerSchemeId)]
    [InlineData("false", AdministrationDocument, Constants.BearerSchemeId)]
    [InlineData("false", ServiceDocument, Constants.PublicWithInternalTokenSchemeId)]
    public void Should_SelectTheSchemeByTheFlag_When_AnAnonymousCallerRequestsADocument(string v1ApiFlag, string path, string expectedScheme)
    {
        using var provider = BuildServices(v1ApiFlag);
        var selector = provider.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get(Constants.DynamicSchemeId)
            .ForwardDefaultSelector;
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Path = path;

        Assert.NotNull(selector);
        Assert.Equal(expectedScheme, selector(httpContext));
    }

    /// <summary>
    /// The gateway's authentication registration, with the IdP settings it requires and the flag as the
    /// host's configuration carries it.
    /// </summary>
    private static ServiceProvider BuildServices(string? v1ApiFlag)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var settings = new Dictionary<string, string?>
        {
            ["Idp:Authority"] = "https://idp.example.test",
            ["Idp:Audience"] = "userorg",
        };

        if (v1ApiFlag is not null)
        {
            settings["FeatureManagement:V1Api"] = v1ApiFlag;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddAuthenticationServices();

        return builder.Services.BuildServiceProvider();
    }
}