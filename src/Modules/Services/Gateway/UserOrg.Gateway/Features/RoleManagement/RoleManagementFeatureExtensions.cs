using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Features.RoleManagement.Components;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Extension methods for wiring the Role Management Blazor SSR feature.
/// </summary>
internal static class RoleManagementFeatureExtensions
{
    /// <summary>
    /// Registers Blazor SSR primitives and in-process Role Management service.
    /// </summary>
    public static IServiceCollection AddRoleManagementFeature(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RoleManagementOptions>()
            .Bind(configuration.GetSection(RoleManagementOptions.SectionName))
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }, "Invalid RoleManagement configuration")
            .ValidateOnStart();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<RoleManagementOptions>>().Value);

        // If Gateway:PathBase is configured, prefer PathBase at runtime and avoid duplicating prefix in Blazor base href.
        services.PostConfigure<RoleManagementOptions>(opts =>
        {
            var gatewayPathBase = configuration["Gateway:PathBase"] ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(gatewayPathBase))
            {
                // When hosting under a PathBase the server sets Request.PathBase; keep PathPrefix empty so base href is "/"
                opts.PathPrefix = string.Empty;
            }
        });

        services.AddScoped<RoleManagementService>();
        services.AddScoped<RoleManagementAuditActor>();
        services.AddScoped<IRoleManagementService>(sp => ActivatorUtilities.CreateInstance<AuditingRoleManagementService>(sp, sp.GetRequiredService<RoleManagementService>()));

        // Seed RBAC data from /app/rbac/seed.json on first startup (skipped when file absent or DB non-empty)
        services.AddHostedService<RbacSeedHostedService>();

        services.AddScoped<RoleManagementState>();
        services.AddSingleton<RoleManagementCircuitGuard>();

        services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .RegisterPersistentService<RoleManagementState>(RenderMode.InteractiveServer);

        return services;
    }

    /// <summary>
    /// Maps the Role Management UI under the /role-management path and protects it with authorization.
    /// </summary>
    public static IEndpointRouteBuilder MapRoleManagementFeature(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapRazorComponents<RoleManagementApp>()
            .AddInteractiveServerRenderMode()
            .RequireAuthorization(Constants.RoleManagementAccessPolicyName)
            .WithOrder(0);

        app.MapPost("/role-management/signout", RoleManagementSignOutApi.SignOutAsync)
            .RequireAuthorization(Constants.RoleManagementAccessPolicyName)
            .WithName("RoleManagementSignOut")
            .WithDescription("Ends the operator's console session");

        // Minimal API for RBAC matrix consumed by frontend
        app.MapGet("/api/role-management/rbac", (IRoleManagementService svc, CancellationToken ct) => RoleManagementApi.GetRbacEndpoint(svc, ct))
            .RequireAuthorization(Constants.RoleManagementAccessPolicyName)
            .WithName("GetRoleManagementRbac")
            .WithDescription("Returns RBAC matrix for role management");

        // Server-side HTML export endpoint for RBAC matrix (download)
        app.MapGet("/api/role-management/rbac/pdf", (IRoleManagementService svc, CancellationToken ct) => RoleManagementExportApi.GetRbacHtmlAsync(svc, ct))
            .AllowAnonymous()
            .WithName("GetRoleManagementRbacHtml")
            .WithDescription("Returns RBAC matrix as downloadable HTML");

        return app;
    }
}