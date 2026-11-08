using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;

using UserOrganizationManagement.API;
using UserOrganizationManagement.API.API;
using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The v1 <see cref="ProjectEndpoint"/> membership routes name the IdP ids whose project scope they changed in
/// the permissions-changed header once their <see cref="IProjectService"/> call has returned, and send nothing
/// when that call throws, because then nothing changed.
/// </summary>
public sealed class ProjectEndpointPermissionsChangedTests
{
    private const string ChangedHeader = "X-UserOrg-Permissions-Changed";
    private const string CapabilityHeader = "X-UserOrg-Gateway-Capabilities";
    private const string CallerIdp = "caller-idp";
    private const string RouteIdp = "route-idp";

    private sealed class NoOpAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestNoOp";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    public enum Route
    {
        CreateProject,
        ChangeRole,
        DeleteProject,
        AssignUser,
        UnassignUser,
    }

    public static TheoryData<Route> Routes() => new()
    {
        Route.CreateProject, Route.ChangeRole, Route.DeleteProject, Route.AssignUser, Route.UnassignUser,
    };

    private static async Task<WebApplication> CreateTestAppAsync(IProjectService service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "Application.Tests"
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        builder.Services.AddHybridCache();

        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUserIdByIdpUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var unitOfWork = new Mock<IUserOrgUnitOfWork>();
        unitOfWork.Setup(u => u.UserRepository).Returns(users.Object);
        builder.Services.AddSingleton(unitOfWork.Object);

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services
            .AddAuthentication(NoOpAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>(NoOpAuthenticationHandler.SchemeName, null);

        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, CallerIdp),
                    new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)),
                ], "Test"));
            await next();
        });
        new ProjectEndpoint().Register(app.MapGroup(""));

        await app.StartAsync();
        return app;
    }

    private static Mock<IProjectService> Service()
    {
        var service = new Mock<IProjectService>();
        service.Setup(s => s.GetProjectRole(It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20));
        return service;
    }

    private static void FailTheServiceCall(Mock<IProjectService> service, Route route)
    {
        var failure = new InvalidOperationException("service down");
        switch (route)
        {
            case Route.CreateProject:
            case Route.AssignUser:
                service.Setup(s => s.AssignedUserToProjectWithRoleAsync(
                        It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);
                break;
            case Route.ChangeRole:
                service.Setup(s => s.EditAssignedUserSystemRole(
                        It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);
                break;
            case Route.DeleteProject:
                service.Setup(s => s.DeleteProjectAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                break;
            case Route.UnassignUser:
                service.Setup(s => s.UnAssignedUserFromProjectAsync(
                        It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(failure);
                break;
        }
    }

    private static HttpRequestMessage RequestFor(Route route) => route switch
    {
        Route.CreateProject => new HttpRequestMessage(HttpMethod.Post, "/Project")
        {
            Content = JsonContent.Create(new ProjectCreate
            {
                Name = "Project One",
                ShortName = "PROJ-1",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                DomainId = 1,
            }),
        },
        Route.ChangeRole => new HttpRequestMessage(HttpMethod.Put, $"/Project/PROJ-1/User/{RouteIdp}/Assign/3"),
        Route.DeleteProject => new HttpRequestMessage(HttpMethod.Delete, "/Project/PROJ-1"),
        Route.AssignUser => new HttpRequestMessage(HttpMethod.Put, $"/Project/PROJ-1/User/{RouteIdp}"),
        Route.UnassignUser => new HttpRequestMessage(HttpMethod.Delete, $"/Project/PROJ-1/User/{RouteIdp}"),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
    };

    private static string ExpectedValue(Route route) => route switch
    {
        Route.CreateProject => CallerIdp,
        Route.DeleteProject => "*",
        _ => RouteIdp,
    };

    private static async Task<HttpResponseMessage> SendAsync(WebApplication app, Route route, bool withCapability = true)
    {
        using var client = app.GetTestClient();
        using var request = RequestFor(route);
        if (withCapability)
            request.Headers.TryAddWithoutValidation(CapabilityHeader, "permissions-changed");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Should_NameTheChangedIds_When_TheServiceCallReturns(Route route)
    {
        await using var app = await CreateTestAppAsync(Service().Object);

        using var response = await SendAsync(app, route);

        Assert.True(response.IsSuccessStatusCode, $"{route} answered {(int)response.StatusCode}");
        Assert.True(response.Headers.TryGetValues(ChangedHeader, out var values), $"{route} sent no {ChangedHeader}");
        Assert.Equal([ExpectedValue(route)], values);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Should_SendNothing_When_TheServiceCallThrows(Route route)
    {
        var service = Service();
        FailTheServiceCall(service, route);
        await using var app = await CreateTestAppAsync(service.Object);

        using var response = await SendAsync(app, route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains(ChangedHeader));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Should_SendNothing_When_TheGatewayDoesNotAdvertiseTheCapability(Route route)
    {
        await using var app = await CreateTestAppAsync(Service().Object);

        using var response = await SendAsync(app, route, withCapability: false);

        Assert.True(response.IsSuccessStatusCode, $"{route} answered {(int)response.StatusCode}");
        Assert.False(response.Headers.Contains(ChangedHeader));
    }
}