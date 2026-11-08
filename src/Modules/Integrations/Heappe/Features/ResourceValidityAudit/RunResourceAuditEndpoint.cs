using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// On-demand audit endpoint (platform admin, enforced by the Gateway RBAC route mapping). Gated by the
/// <c>HeappeResourceAudit</c> feature flag via <c>.EnabledWhen</c>. <c>delivery=stream</c> (default) streams one
/// result per processed target; <c>delivery=webhook</c> runs detached, returns 202, and posts the findings card.
/// </summary>
public static class RunResourceAuditEndpoint
{
    private const string WebhookDelivery = "webhook";
    private const string LoggerCategory = "Heappe.ResourceValidityAudit.ManualRun";

    internal static IResult HandleAsync(
        [FromQuery(Name = "delivery")] string? delivery,
        [FromServices] IHeappeResourceAuditService audit,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] IHostApplicationLifetime lifetime,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (string.Equals(delivery, WebhookDelivery, StringComparison.OrdinalIgnoreCase))
        {
            // Detached so the HTTP response is instant (dodges proxy timeouts). Uses the application-stopping
            // token — NOT the request token, which is cancelled the moment we return 202.
            _ = Task.Run(() => RunDetachedAsync(scopeFactory, loggerFactory, lifetime.ApplicationStopping), CancellationToken.None);
            return TypedResults.Accepted((string?)null);
        }

        // Stream one TargetAuditResult per processed target; the framework flushes each item as it is produced.
        return TypedResults.Ok(audit.RunAuditAsync(ct));
    }

    private static async Task RunDetachedAsync(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IHeappeResourceAuditService>();
            var webhook = scope.ServiceProvider.GetRequiredService<HeappeResourceAuditWebhook>();
            await HeappeResourceAuditRun.CollectAndNotifyAsync(audit, webhook, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Detached HEAppE resource audit run failed.");
        }
    }

    public static IEndpointRouteBuilder MapRunResourceAuditEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("ResourceAudit/run", HandleAsync)
           .EnabledWhen(HeappeResourceAuditWorker.FeatureFlagName)
           .MapDocumentation();
        return app;
    }

    private static RouteHandlerBuilder MapDocumentation(this RouteHandlerBuilder builder) => builder
        .WithName("RunHeappeResourceAudit")
        .WithSummary("Runs the HEAppE resource validity audit on demand. Platform admin only.")
        .WithDescription(
            "Runs the resource validity audit immediately instead of waiting for its scheduled run. For every audited target it checks that the HEAppE project record exists and that its validity window matches the one userorg holds, and reports the expected and actual dates with a status; the audit is read-only and never writes back to HEAppE. How the outcome is delivered is chosen with the delivery query parameter. The route is platform-admin only (enforced by the Gateway RBAC mapping) and is published only while the HeappeResourceAudit feature flag is on.")
        .WithOpenApiDoc(operation =>
        {
            var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == "delivery");
            if (parameter is not null)
            {
                parameter.Description = "How the result is delivered. Omitted or any other value streams the audit in the response, one result per processed target, flushed as it is produced. 'webhook' (case-insensitive) instead answers 202 immediately and runs the audit detached from the request, posting the findings card to the configured webhook when it completes; the response then carries no results at all.";
            }

            return operation;
        })
        .WithTags("Heappe")
        .Produces<IReadOnlyList<TargetAuditResult>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
}