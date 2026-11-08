using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
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
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// B02: GET /Administration/Project resolves the provider scope from the caller's claims (via
/// <see cref="AuthenticationExtensions.GetCallerInfo"/>) BEFORE calling <see cref="IProjectService"/>.
/// Platform admin takes precedence — scope null; else the caller's own provider; a caller with
/// neither identity is refused before the service is ever touched (fail closed).
/// </summary>
public sealed class AdministrationEndpointProjectListTests
{
    /// <summary>
    /// A no-op authentication handler: it never authenticates a request itself (the caller's identity is
    /// injected directly onto <c>HttpContext.User</c> by test middleware), it only exists so
    /// <c>Results.Forbid()</c> has a default scheme to resolve.
    /// </summary>
    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestNoOp";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    private static async Task<WebApplication> CreateTestAppAsync(IProjectService service, ClaimsPrincipal caller)
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
        new AdministrationEndpoint().Register(app.MapGroup(""));

        await app.StartAsync();
        return app;
    }

    private static ClaimsPrincipal PlatformAdmin() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, RoleInfoConstants.PlatformAdministratorRoleName)], "Test"));

    private static ClaimsPrincipal ProviderAdmin(Guid providerId, string idpUserId) => new(new ClaimsIdentity(
        [
            new Claim(AuthConstants.ProviderIdClaimType, providerId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, idpUserId),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken))
        ], "Test"));

    private static ClaimsPrincipal NeitherIdentity() => new(new ClaimsIdentity());

    /// <summary>
    /// A platform admin who also happens to carry a <c>provider_id</c> claim — the one principal that can
    /// tell the two branches of the precedence cascade apart. Only the platform-admin branch may win.
    /// </summary>
    private static ClaimsPrincipal PlatformAdminWithProviderId(Guid providerId) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, RoleInfoConstants.PlatformAdministratorRoleName),
            new Claim(AuthConstants.ProviderIdClaimType, providerId.ToString())
        ], "Test"));

    /// <summary>
    /// The service call the endpoint makes, named by the two arguments under test — the provider scope
    /// (<c>null</c> meaning unscoped) and the caller's identity-provider user id (<c>null</c> on the
    /// platform-admin branch). Shared by Setup and Verify so both always describe the same call.
    /// </summary>
    private static Expression<Func<IProjectService, Task<IList<ProjectModel>>>> ProjectListScopedTo(Guid? providerScope, string? callerIdpUserId)
        => s => s.GetProjectsAsync(null, null, null, null, null, providerScope, It.IsAny<CancellationToken>(), callerIdpUserId);

    [Fact(DisplayName = "Platform admin: providerScope=null and callerIdpUserId=null are passed to the service, 200 with the unscoped result")]
    public async Task ReturnsUnscopedList_WhenCallerIsPlatformAdmin()
    {
        var mockService = new Mock<IProjectService>();
        mockService
            .Setup(ProjectListScopedTo(null, null))
            .ReturnsAsync(new List<ProjectModel> { new() { ShortName = "unscoped" } });

        await using var app = await CreateTestAppAsync(mockService.Object, PlatformAdmin());
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/Administration/Project");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ProjectModel>>();
        Assert.Single(body!);

        mockService.Verify(ProjectListScopedTo(null, null), Times.Once);
    }

    [Fact(DisplayName = "ProviderAdmin: providerScope == caller.ProviderId and callerIdpUserId == caller.IdentityProviderUserId are passed to the service, 200 with the scoped result")]
    public async Task ReturnsScopedList_WhenCallerIsProviderAdmin()
    {
        var providerId = Guid.NewGuid();
        var idpUserId = $"idp-{Guid.NewGuid()}";
        var mockService = new Mock<IProjectService>();
        mockService
            .Setup(ProjectListScopedTo(providerId, idpUserId))
            .ReturnsAsync(new List<ProjectModel> { new() { ShortName = "scoped" } });

        await using var app = await CreateTestAppAsync(mockService.Object, ProviderAdmin(providerId, idpUserId));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/Administration/Project");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ProjectModel>>();
        Assert.Single(body!);

        mockService.Verify(ProjectListScopedTo(providerId, idpUserId), Times.Once);
    }

    [Fact(DisplayName = "Platform admin who also carries a provider_id: platform-admin branch still wins, providerScope=null and callerIdpUserId=null")]
    public async Task ReturnsUnscopedList_WhenCallerIsPlatformAdminAndAlsoCarriesProviderId()
    {
        var mockService = new Mock<IProjectService>();
        mockService
            .Setup(ProjectListScopedTo(null, null))
            .ReturnsAsync(new List<ProjectModel> { new() { ShortName = "unscoped" } });

        await using var app = await CreateTestAppAsync(mockService.Object, PlatformAdminWithProviderId(Guid.NewGuid()));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/Administration/Project");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ProjectModel>>();
        Assert.Single(body!);

        mockService.Verify(ProjectListScopedTo(null, null), Times.Once);
    }

    [Fact(DisplayName = "Caller with neither identity: 403 before the service is invoked (fail closed)")]
    public async Task ReturnsForbidden_AndNeverCallsService_WhenCallerHasNeitherIdentity()
    {
        var mockService = new Mock<IProjectService>();

        await using var app = await CreateTestAppAsync(mockService.Object, NeitherIdentity());
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/Administration/Project");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(mockService.Invocations);
    }
}
