namespace UserOrg.Gateway.Startup;

/// <summary>
/// Extension methods for registering the RoleManagementAuthMiddleware.
/// </summary>
internal static class RoleManagementAuthMiddlewareExtensions
{
    extension(IApplicationBuilder app)
    {
        /// <summary>
        /// Adds the RoleManagementAuthMiddleware to the application pipeline.
        /// Must be registered after UseAuthentication() and UseAuthorization(),
        /// but before MapRoleManagementFeature().
        /// </summary>
        public IApplicationBuilder UseRoleManagementAuth()
        {
            return app.UseMiddleware<RoleManagementAuthMiddleware>();
        }
    }
}