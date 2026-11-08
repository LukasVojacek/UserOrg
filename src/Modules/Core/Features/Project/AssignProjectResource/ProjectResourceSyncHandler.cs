using Core.Features.Provider.ResourceAssignment;

namespace Core.Features.Project.AssignProjectResource;

/// <summary>
/// Outbox-dispatched handler for <see cref="ProjectResourceAssignmentChangedEvent"/>: runs exactly one HEAppE
/// sync for the single affected project <b>to completion</b> through the Core-owned
/// <see cref="IResourceSyncRunner"/> port (ADR-0040). Mirrors <see cref="ResourceSyncHandler"/>, but the event
/// already names the one project and resource, so no resolving query is needed. The event is durable
/// (ADR-0028), so the outbox dispatcher invokes this; a sync failure throws and the row is retried.
/// </summary>
internal sealed class ProjectResourceSyncHandler(IResourceSyncRunner syncRunner)
    : IDomainEventHandler<ProjectResourceAssignmentChangedEvent>
{
    public async Task HandleAsync(ProjectResourceAssignmentChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await syncRunner.RunResourceSyncAsync(domainEvent.ShortName, domainEvent.ResourceName, cancellationToken);
    }
}