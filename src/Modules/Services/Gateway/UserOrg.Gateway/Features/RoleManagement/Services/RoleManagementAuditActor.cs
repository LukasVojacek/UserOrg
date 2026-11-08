using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.AspNetCore.Components.Authorization;

namespace UserOrg.Gateway.Features.RoleManagement.Services;

internal sealed class RoleManagementAuditActor(IServiceProvider services)
{
    internal const string ServiceActorId = "gateway";

    internal const string ServiceActorType = "service";

    private bool _actsAsService;

    internal void ActAsService() => _actsAsService = true;

    internal async ValueTask<AuditActor> ResolveActorAsync()
    {
        if (_actsAsService)
        {
            return new AuditActor(ServiceActorId, ServiceActorType, SessionId: null);
        }

        var state = await services.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true || state.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is not { Length: > 0 } sub)
        {
            throw new UnauthorizedAccessException("Role management requires an authenticated operator.");
        }

        return new AuditActor(sub, RoleManagementSessionAudit.OperatorActorType, state.User.FindFirstValue(Constants.RoleManagementAccessIdClaim));
    }
}

internal readonly record struct AuditActor(string ActorId, string ActorType, string? SessionId);