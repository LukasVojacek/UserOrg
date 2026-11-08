using Lukdrasil.Error;

namespace Heappe.Features.LocationPartitionsSynchronization;

internal static partial class Errors
{
    internal static Error NoChangesPersistedWhileSynchronizingLocationPartitions(Guid locationId) => new ErrorBuilder()
        .WithDescription("No changes persisted while synchronizing location partitions for location '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(locationId);
}