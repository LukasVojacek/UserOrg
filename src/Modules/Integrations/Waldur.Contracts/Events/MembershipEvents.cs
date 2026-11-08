using Waldur.Contracts.Payloads;

namespace Waldur.Contracts.Events;

/// <summary>Emitted when a <c>(project, userName)</c> membership is not yet in the SHA map.</summary>
public sealed record UserAssigned(ProjectUserPayload Payload) : WaldurIntegrationEvent;

/// <summary>Emitted when a known membership's hashed fields (role/access) differ.</summary>
public sealed record UserRoleChanged(ProjectUserPayload Payload) : WaldurIntegrationEvent;

/// <summary>
/// Emitted when a user is missing from a successfully fetched, complete member snapshot
/// (ADR-0054/A2). Identity is <c>(Instance, ProjectExternalId, UserName)</c>; replay against an
/// already-absent assignment is a required no-op.
/// </summary>
public sealed record UserRemoved(string ProjectExternalId, string UserName) : WaldurIntegrationEvent;