using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;

using UserOrganizationManagement.API.API;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Provider;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The v1 counterpart of ADR-0105 for the two provider writes that trusted the request over the identity:
/// <c>PUT /Provider</c> checked only that a <c>provider_id</c> claim was present and then updated the
/// <em>body</em>-named provider, and <c>DELETE /Provider/{Id}</c> had no authorization at all.
/// A caller carrying a <c>provider_id</c> may now write only that provider. A caller carrying none keeps
/// its previous reach on each route — refused on <c>PUT</c>, allowed on <c>DELETE</c>, where the platform
/// admin is gated by the Gateway's <c>perm:provider:remove</c> instead.
/// </summary>
public sealed class ProviderEndpointWriteAuthorizationTests
{
    /// <summary>
    /// A no-op authentication handler: the caller's identity is injected onto <c>HttpContext.User</c> by
    /// test middleware, so this exists only to give <c>Results.Forbid()</c> a scheme to resolve.
    /// </summary>
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestNoOp";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<WebApplication> CreateTestAppAsync(IProviderService service, ClaimsPrincipal caller)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "Application.Tests"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        builder.Services
            .AddAuthentication(NoOpAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>(NoOpAuthenticationHandler.SchemeName, null);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = caller;
            await next();
        });
        new ProviderEndpoint().Register(app.MapGroup(""));

        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// A caller whose <c>provider_id</c> claim names <paramref name="providerId"/> — what
    /// <c>TryGetProviderId</c> reads, and the only thing it reads besides the identity being authenticated.
    /// </summary>
    private static ClaimsPrincipal ProviderIdentity(Guid providerId) => new(new ClaimsIdentity(
        [
            new Claim(AuthConstants.ProviderIdClaimType, providerId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, "idp-provider-caller"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken))
        ], "Test"));

    /// <summary>A caller with no <c>provider_id</c> claim: the platform-admin shape the Gateway gates.</summary>
    private static ClaimsPrincipal NoProviderIdentity() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, RoleInfoConstants.PlatformAdministratorRoleName)], "Test"));

    private static ProviderUpdate UpdateOf(Guid providerId) => new()
    {
        Id = providerId,
        Name = "target",
        CountryCode = "CZ"
    };

    [Fact(DisplayName = "PUT /Provider: a provider identity updating a provider its claim does not name is refused before the service is touched")]
    public async Task Put_ReturnsForbidden_AndNeverCallsService_WhenBodyNamesAnotherProvider()
    {
        var mockService = new Mock<IProviderService>();

        await using var app = await CreateTestAppAsync(mockService.Object, ProviderIdentity(Guid.NewGuid()));
        using var client = app.GetTestClient();

        using var response = await client.PutAsJsonAsync(new Uri("/Provider", UriKind.Relative), UpdateOf(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(mockService.Invocations);
    }

    [Fact(DisplayName = "PUT /Provider: a provider identity updating its own provider is allowed")]
    public async Task Put_UpdatesProvider_WhenBodyNamesTheCallersOwnProvider()
    {
        var providerId = Guid.NewGuid();
        var mockService = new Mock<IProviderService>();

        await using var app = await CreateTestAppAsync(mockService.Object, ProviderIdentity(providerId));
        using var client = app.GetTestClient();

        using var response = await client.PutAsJsonAsync(new Uri("/Provider", UriKind.Relative), UpdateOf(providerId), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mockService.Verify(s => s.UpdateProviderAsync(It.Is<ProviderUpdate>(u => u.Id == providerId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PUT /Provider: a caller with no provider_id claim stays refused, as before")]
    public async Task Put_ReturnsForbidden_WhenCallerHasNoProviderIdClaim()
    {
        var mockService = new Mock<IProviderService>();

        await using var app = await CreateTestAppAsync(mockService.Object, NoProviderIdentity());
        using var client = app.GetTestClient();

        using var response = await client.PutAsJsonAsync(new Uri("/Provider", UriKind.Relative), UpdateOf(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(mockService.Invocations);
    }

    [Fact(DisplayName = "DELETE /Provider/{Id}: a provider identity deleting a provider its claim does not name is refused before the service is touched")]
    public async Task Delete_ReturnsForbidden_AndNeverCallsService_WhenRouteNamesAnotherProvider()
    {
        var mockService = new Mock<IProviderService>();

        await using var app = await CreateTestAppAsync(mockService.Object, ProviderIdentity(Guid.NewGuid()));
        using var client = app.GetTestClient();

        using var response = await client.DeleteAsync(new Uri($"/Provider/{Guid.NewGuid()}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(mockService.Invocations);
    }

    [Fact(DisplayName = "DELETE /Provider/{Id}: a provider identity deleting its own provider is allowed")]
    public async Task Delete_DeletesProvider_WhenRouteNamesTheCallersOwnProvider()
    {
        var providerId = Guid.NewGuid();
        var mockService = new Mock<IProviderService>();

        await using var app = await CreateTestAppAsync(mockService.Object, ProviderIdentity(providerId));
        using var client = app.GetTestClient();

        using var response = await client.DeleteAsync(new Uri($"/Provider/{providerId}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        mockService.Verify(s => s.DeleteProviderAsync(providerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The platform-admin delete this fix must not take away: no <c>provider_id</c> claim means the
    /// Gateway's <c>perm:provider:remove</c> is the gate, and Core leaves that reach alone.
    /// </summary>
    [Fact(DisplayName = "DELETE /Provider/{Id}: a caller with no provider_id claim keeps its reach on any provider")]
    public async Task Delete_DeletesProvider_WhenCallerHasNoProviderIdClaim()
    {
        var providerId = Guid.NewGuid();
        var mockService = new Mock<IProviderService>();

        await using var app = await CreateTestAppAsync(mockService.Object, NoProviderIdentity());
        using var client = app.GetTestClient();

        using var response = await client.DeleteAsync(new Uri($"/Provider/{providerId}", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        mockService.Verify(s => s.DeleteProviderAsync(providerId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
