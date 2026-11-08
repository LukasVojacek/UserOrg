using Lukdrasil.StepUpLogging;

namespace UserOrg.Gateway.Features.RoleManagement.Services;

/// <summary>
/// Reconciles RBAC data from the seed file on every startup (ADR-0060).
/// The import is additive: missing permissions, endpoint mappings, and roles are inserted while existing
/// entries are never modified or deleted (<c>overwriteExisting: false</c>). An empty store degenerates to a
/// full import. When anything is inserted the endpoint-permission snapshot cache is invalidated so the
/// running Gateway picks up the additions without a rebuild.
/// In non-Docker environments (e.g., Aspire dev), the seed file is absent and the service skips silently.
/// Configure the seed file path via <c>RoleManagement:SeedFilePath</c> (env: <c>RoleManagement__SeedFilePath</c>).
/// </summary>
internal sealed class RbacSeedHostedService(
    IServiceScopeFactory scopeFactory,
    RoleManagementOptions options,
    ILogger<RbacSeedHostedService> logger) : IHostedService
{
    // Seams for unit testing — production code uses real File I/O
    internal Func<string, bool> FileExists { get; init; } = File.Exists;
    internal Func<string, Stream> OpenRead { get; init; } = path => File.OpenRead(path);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var seedFilePath = options.SeedFilePath;

        if (!FileExists(seedFilePath))
        {
            logger.LogInformation("RBAC seed file not found at {Path}, skipping.", seedFilePath);
            return;
        }

        try
        {
            scope.ServiceProvider.GetService<RoleManagementAuditActor>()?.ActAsService();
            var service = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();

            logger.LogInformation("Reconciling RBAC data from {Path} (additive, existing entries preserved).", seedFilePath);

            await using var fileStream = OpenRead(seedFilePath);
            var importResult = await service.ImportRolesAsync(fileStream, overwriteExisting: false, cancellationToken);

            if (!importResult.IsSuccess)
            {
                logger.LogWarning("RBAC reconciliation import failed: {Error}", importResult.Error);
                await AuditSeededAsync(scope.ServiceProvider, SeededFailure());
                return;
            }

            var summary = importResult.Value;
            logger.LogInformation(
                "RBAC reconciliation completed: {Created} created, {Updated} updated, {Errors} errors.",
                summary.CreatedCount, summary.UpdatedCount, summary.ErrorCount);

            if (summary.CreatedCount > 0)
            {
                await service.InvalidateEndpointPermissionCacheAsync(cancellationToken);
                logger.LogInformation("Endpoint-permission snapshot cache invalidated after {Created} insertions.", summary.CreatedCount);
            }

            await AuditSeededAsync(scope.ServiceProvider, AuditEvent.Success(GatewayAuditActions.RbacSeeded, RoleManagementAuditActor.ServiceActorId, RoleManagementAuditActor.ServiceActorType) with
            {
                Data = new Dictionary<string, object?>
                {
                    ["created"] = summary.CreatedCount,
                    ["updated"] = summary.UpdatedCount,
                    ["errors"] = summary.ErrorCount,
                },
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RBAC seed failed unexpectedly, application will continue without seeding.");
            await AuditSeededAsync(scope.ServiceProvider, SeededFailure());
        }
    }

    private static AuditEvent SeededFailure()
        => AuditEvent.Failure(GatewayAuditActions.RbacSeeded, RoleManagementAuditActor.ServiceActorId, RoleManagementAuditActor.ServiceActorType);

    private Task AuditSeededAsync(IServiceProvider services, AuditEvent auditEvent)
        => services.GetService<IAuditLogger<RbacSeedHostedService>>() is { } auditLogger
            ? auditLogger.AuditBestEffortAsync(auditEvent, logger)
            : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}