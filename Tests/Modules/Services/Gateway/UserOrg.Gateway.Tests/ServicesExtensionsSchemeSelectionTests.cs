using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Startup;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the dynamic policy scheme's forward selector registered by
/// <see cref="ServicesExtensions.AddAuthenticationServices"/>: role-management paths go to the cookie scheme,
/// public endpoints to the public scheme, then the Authorization header decides, and no header falls back to
/// Bearer.
/// </summary>
public sealed class ServicesExtensionsSchemeSelectionTests
{
    private const string PrivatePath = "/api/User/Preferences";

    private const string ServiceDocument = "/api/Service/openapi.json";

    private const string RoleManagementCookie = "RoleManagementAuth=session";

    [Theory]
    [InlineData("/Role-Management/users", null)]
    [InlineData("/Role-Management/users", RoleManagementCookie)]
    [InlineData("/_FRAMEWORK/blazor.web.js", null)]
    [InlineData("/_FRAMEWORK/blazor.web.js", RoleManagementCookie)]
    [InlineData("/API/Role-Management/RBAC/PDF", null)]
    [InlineData("/API/Role-Management/RBAC/PDF", RoleManagementCookie)]
    [InlineData("/_Blazor/negotiate", null)]
    [InlineData("/_Blazor/negotiate", RoleManagementCookie)]
    public void Should_SelectTheRoleManagementCookieScheme_When_ThePathIsRoleManagement_WithOrWithoutCookie(string path, string? cookie)
    {
        using var provider = BuildServices("Production");
        var httpContext = BuildRequest(provider, path, "Bearer some-token");
        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = cookie;
        }

        Assert.Equal(Constants.RoleManagementCookieSchemeId, Select(provider, httpContext));
    }

    [Fact]
    public void Should_SelectThePublicScheme_When_APublicEndpointCarriesABearerHeader()
    {
        using var provider = BuildServices("Production");
        var httpContext = BuildRequest(provider, ServiceDocument, "Bearer some-token");

        Assert.Equal(Constants.PublicWithInternalTokenSchemeId, Select(provider, httpContext));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Should_SelectTheBearerScheme_When_ABearerHeaderArrivesWithTheOverrideOff(string environment)
    {
        using var provider = BuildServices(environment, devOverrideEnabled: "false");
        var httpContext = BuildRequest(provider, PrivatePath, "Bearer some-token");

        Assert.Equal(Constants.BearerSchemeId, Select(provider, httpContext));
    }

    [Fact]
    public void Should_SelectTheDevOverrideScheme_When_ABearerHeaderArrivesInDevelopmentWithTheOverrideOn()
    {
        using var provider = BuildServices("Development", devOverrideEnabled: "true");
        var httpContext = BuildRequest(provider, PrivatePath, "Bearer idp-user-1");

        Assert.Equal(Constants.DevOverrideSchemeId, Select(provider, httpContext));
    }

    [Fact]
    public void Should_SelectTheBearerScheme_When_ABearerHeaderArrivesInProductionWithTheOverrideOn()
    {
        using var provider = BuildServices("Production", devOverrideEnabled: "true");
        var httpContext = BuildRequest(provider, PrivatePath, "Bearer idp-user-1");

        Assert.Equal(Constants.BearerSchemeId, Select(provider, httpContext));
    }

    [Theory]
    [InlineData("Service x")]
    [InlineData("provider x")]
    [InlineData("Basic x")]
    [InlineData("bare-token")]
    public void Should_SelectTheApiKeyScheme_When_TheAuthorizationHeaderIsNotBearer(string authorization)
    {
        using var provider = BuildServices("Production");
        var httpContext = BuildRequest(provider, PrivatePath, authorization);

        Assert.Equal(Constants.ApiKeySchemeId, Select(provider, httpContext));
    }

    [Fact]
    public void Should_FallBackToTheBearerScheme_When_APrivatePathHasNoAuthorizationHeader()
    {
        using var provider = BuildServices("Production");
        var httpContext = BuildRequest(provider, PrivatePath, authorization: null);

        Assert.Equal(Constants.BearerSchemeId, Select(provider, httpContext));
    }

    private static string Select(ServiceProvider provider, HttpContext httpContext)
    {
        var selector = provider.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get(Constants.DynamicSchemeId)
            .ForwardDefaultSelector;

        Assert.NotNull(selector);
        return selector(httpContext)!;
    }

    private static DefaultHttpContext BuildRequest(ServiceProvider provider, string path, string? authorization)
    {
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Path = path;
        if (authorization is not null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        return httpContext;
    }

    /// <summary>
    /// The gateway's authentication registration in the given environment, with the IdP settings it requires
    /// and the Develop-only override flag as the host's configuration carries it.
    /// </summary>
    private static ServiceProvider BuildServices(string environment, string devOverrideEnabled = "false")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Idp:Authority"] = "https://idp.example.test",
            ["Idp:Audience"] = "userorg",
            ["DevAuthOverride:Enabled"] = devOverrideEnabled,
        });
        builder.AddAuthenticationServices();

        return builder.Services.BuildServiceProvider();
    }
}
