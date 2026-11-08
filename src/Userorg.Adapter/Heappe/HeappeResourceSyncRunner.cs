using Core.Domain.Project;
using Core.Domain.Provider;
using Core.Features.Provider.ResourceAssignment;

using Heappe.Features.CommandTemplateSynchronization;

using Microsoft.Extensions.Logging;

namespace Userorg.Adapter.Heappe;

/// <summary>
/// Runs a HEAppE resource sync to completion (ADR-0040) by delegating to the HEAppE module's
/// <see cref="IProjectCommandTemplateSyncService"/> and throwing when it reports failure, so the outbox
/// dispatcher leaves the row undispatched and retries. The HEAppE coupling stays confined to the adapter.
/// </summary>
internal sealed class HeappeResourceSyncRunner(
    ILogger<HeappeResourceSyncRunner> logger,
    IProjectCommandTemplateSyncService syncService) : IResourceSyncRunner
{
    public async Task RunResourceSyncAsync(ProjectShortName projectShortName, ResourceName resourceName, CancellationToken ct)
    {
        var result = await syncService.SyncCommandTemplatesForResource(projectShortName.Value, resourceName.Value, ct);
        if (!result.IsSuccess)
        {
            // Throw so the outbox dispatcher leaves the row undispatched and retries with backoff (ADR-0040).
            // The HEAppE sync is idempotent (keyed by project+resource, upsert), so a redelivery converges.
            throw new InvalidOperationException(
                $"HEAppE command-template sync failed for project '{projectShortName.Value}', resource '{resourceName.Value}'.");
        }

        logger.LogInformation("HEAppE resource sync completed: {Project}/{Resource}", projectShortName.Value, resourceName.Value);
    }
}