namespace Core.Features.Provider.ResourceAssignment;

/// <summary>
/// Core-owned port that runs a HEAppE resource synchronization <b>to completion</b> and <b>throws on
/// failure</b> (ADR-0040). It is awaited
/// by the outbox dispatcher: a thrown exception leaves the outbox row undispatched so it is retried, and a
/// normal return marks the sync delivered. The adapter implements it over the HEAppE sync service; Core
/// depends only on this port and never on the HEAppE module itself.
/// </summary>
public interface IResourceSyncRunner
{
    Task RunResourceSyncAsync(ProjectShortName projectShortName, ResourceName resourceName, CancellationToken ct);
}