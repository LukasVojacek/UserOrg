using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace UserOrg.Gateway.Features.RoleManagement;

internal static class RoleManagementSessionAudit
{
    internal const string OperatorActorType = "user";

    internal const string AnonymousActor = "anonymous";

    internal static string OperatorId(ClaimsPrincipal? principal)
        => principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) is { Length: > 0 } sub ? sub : AnonymousActor;
}