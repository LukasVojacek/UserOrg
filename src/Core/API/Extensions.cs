using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;

namespace UserOrganizationManagement.API;

/// <summary>
/// Extension Class
/// </summary>
internal static class Extensions
{
    /// <summary>
    /// Registration classes into API, that implement IApiRoute
    /// </summary>
    internal static RouteGroupBuilder RegisterApiRoutes(this WebApplication app)
    {
        var group = app.MapGroup("api").RequireAuthorization();
        group.AddEndpointFilter<UserInitializationEndpointFilter>();
        group.AddEndpointFilterFactory(ValidationFilter.ValidationFilterFactory);

        var type = typeof(IApiRoute);
        var types = type.Assembly.GetTypes().Where(p => p.IsClass && p.IsAssignableTo(type));

        foreach (var routeType in types)
        {
            var route = (IApiRoute)Activator.CreateInstance(routeType);
            route.Register(group);
        }

        return group;
    }



}