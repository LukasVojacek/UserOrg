namespace Core.Features.Provider.ResourceAssignment;

/// <summary>
/// Outbox-dispatched handler: when a resource assignment changes, resolves every project using the
/// resource and runs one HEAppE sync per project <b>to completion</b> through the Core-owned
/// <see cref="IResourceSyncRunner"/> port (ADR-0040). Invoked by the outbox dispatcher (the event is
/// durable, ADR-0028); a sync failure throws, so the outbox row stays undispatched and is retried —
/// replacing the legacy fire-and-forget enqueue.
/// </summary>
internal sealed class ResourceSyncHandler(
    IResourceAssignmentQuery query,
    IResourceSyncRunner syncRunner) : IDomainEventHandler<ResourceAssignmentChangedEvent>
{
    public async Task HandleAsync(ResourceAssignmentChangedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var projects = await query.GetProjectsUsingResourceAsync(domainEvent.Resource, cancellationToken);

        foreach (var project in projects)
        {
            await syncRunner.RunResourceSyncAsync(project.ProjectShortName, project.ResourceName, cancellationToken);
        }
    }
}