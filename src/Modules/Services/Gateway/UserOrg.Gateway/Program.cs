using System.Diagnostics;

using Forge.Core;

using HashiCorpVault;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using UserOrg.Gateway;
using UserOrg.Gateway.Features.Authorization;
using UserOrg.Gateway.Features.Diagnostics;
using UserOrg.Gateway.Features.HealthChecks;
using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RouteExistence;
using UserOrg.Gateway.Features.ServiceValidation;
using UserOrg.Gateway.Startup;
using UserOrg.ServiceDefaults;
using UserOrg.ServiceDefaults.Http;

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddGatewaySpanNaming();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

builder.Services.AddOptionsWithValidateOnStart<ServiceToServiceOptions>()
    .BindConfiguration(ServiceToServiceOptions.ConfigurationSection);

builder.Services.AddOptionsWithValidateOnStart<CyclopsOptions>()
    .BindConfiguration(CyclopsOptions.ConfigurationSection);

// Gateway authorization behaviour (GatewayAuthorization: section). Optional — absent
// configuration keeps the default (system roles bypass the project-membership check).
builder.Services.AddOptions<UserOrg.Gateway.Configuration.GatewayAuthorizationOptions>()
    .BindConfiguration(UserOrg.Gateway.Configuration.GatewayAuthorizationOptions.SectionName);

builder.Services.AddSingleton<IServiceCallerValidator, ServiceCallerValidator>();

builder.Services.AddSingleton<IPermissionMatcher, PermissionMatcher>();

// Register per-user permissions cache (30 s TTL) — avoids 3× DB calls per request
builder.Services.AddSingleton<IUserPermissionsCache, UserPermissionsCache>();

builder.AddRouteExistenceFeature();

builder.AddHashiCorpVaultIntegration();
builder.AddForgeCore();

builder.Services.AddRoleManagementFeature(builder.Configuration);

// Configure Blazor Circuit options for pause/resume functionality
builder.Services.Configure<CircuitOptions>(options =>
{
    options.PersistedCircuitInMemoryMaxRetained = 20; // Max 20 circuits in memory
    options.PersistedCircuitInMemoryRetentionPeriod = TimeSpan.FromMinutes(30); // 30 minutes retention
});

builder.Services.AddReverseProxy();

builder.Services.AddServiceDiscovery();
builder.AddAuthenticationServices();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Constants.RoleManagementAccessPolicyName, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Constants.RoleManagementAccessIdClaim);
    });
});

builder.Services.AddHttpForwarderWithServiceDiscovery();

// Named HttpClient for Cyclops customer API — shared via IHttpClientFactory (no per-request socket leak)
builder.Services.AddHttpClient("cyclops-customer", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Register named HttpClient used by the userorg upstream health check.
// Uses service discovery so the Gateway calls the API directly (not through YARP).
builder.Services
    .AddHttpClient(UserOrgApiHealthCheck.HttpClientName, client =>
    {
        client.BaseAddress = new Uri("https+http://userorg");
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddServiceDiscovery();

builder.Services.AddHealthChecks()
    .AddCheck<UserOrgApiHealthCheck>(
        "userorg",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["upstream", "api"]);

var app = builder.Build();
app.UseTrustedProxyForwardedHeaders();

app.MapDefaultEndpoints();

// Configure optional gateway path base (e.g., when hosting under a sub-path like "/userorg")
var gatewayPathBase = builder.Configuration["Gateway:PathBase"]
    ?? string.Empty;
if (!string.IsNullOrWhiteSpace(gatewayPathBase))
{
    if (gatewayPathBase.StartsWith('/') == false)
    {
        gatewayPathBase = $"/{gatewayPathBase}";
    }

    if (gatewayPathBase.EndsWith('/'))
    {
        gatewayPathBase = gatewayPathBase.TrimEnd('/');
    }

    app.UsePathBase(new PathString(gatewayPathBase));
}

app.UseStepUpRequestLogging();

app.UseHttpsRedirection();
app.MapStaticAssets(); // Maps Blazor framework files (_framework/blazor.web.js, etc.) - must be before UseStaticFiles
app.UseStaticFiles();
app.UseAuthentication();

// Add RoleManagement authentication middleware (must be after UseAuthentication but before UseAuthorization)
app.UseRoleManagementAuth();

app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOidcDiagnostics();
}

app.MapInternalTokenEndpoints();

// Map Role Management SSR UI before proxy fallbacks
app.MapRoleManagementFeature();



app.MapCyclopsUsageForwarder();
app.MapCyclopsTotalUsageForwarder();
app.MapCyclopsUsageV1Forwarder();
// Map a Direct Forwarder which forwards requests to the resolved "userorgApi" endpoints
// with production-ready header configuration
// IMPORTANT: This catch-all is registered LAST and will NOT match Blazor endpoints
// because MapRazorComponents (called earlier) already registered routes for:
// - /role-management (page route)
// - /_blazor (SignalR hub)
// - /_framework/* (static files are handled by UseStaticFiles earlier in pipeline)
// Map userorg catch-all forwarder (moved to Startup extensions for readability)
app.MapUserOrgCatchAllForwarder();

await app.RunAsync();
