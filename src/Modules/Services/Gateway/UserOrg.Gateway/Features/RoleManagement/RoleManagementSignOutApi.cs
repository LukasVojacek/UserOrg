using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Features.RoleManagement;

internal static class RoleManagementSignOutApi
{
    internal static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery, IAuditLogger<RoleManagementAuthMiddleware> auditLogger, ILogger<RoleManagementAuthMiddleware> logger)
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        var operatorId = RoleManagementSessionAudit.OperatorId(context.User);
        await context.SignOutAsync(Constants.RoleManagementCookieSchemeId);
        await auditLogger.AuditBestEffortAsync(
            AuditEvent.Success(GatewayAuditActions.RoleManagementSignedOut, operatorId, RoleManagementSessionAudit.OperatorActorType),
            logger);
        return Results.Text("Signed out of role management.");
    }
}