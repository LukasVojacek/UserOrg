using Heappe.Contracts.Entries.Sync;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Heappe.Features.LocationPartitionsSynchronization;

internal interface ILocationPartitionsSynchronizationService
{
    Task<Result<bool, Error>> SynchronizationLocationPartitionsAsync(LocationSyncEntry location, string projectResourceName, CancellationToken ct);
}