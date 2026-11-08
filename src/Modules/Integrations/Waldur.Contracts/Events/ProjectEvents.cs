using Waldur.Contracts.Payloads;

namespace Waldur.Contracts.Events;

/// <summary>Emitted when a project id is not yet in the SHA map.</summary>
public sealed record ProjectCreated(ProjectPayload Payload) : WaldurIntegrationEvent;

/// <summary>
/// Emitted when a known project's payload hash differs — including the transition to
/// <see cref="ProjectPayload.IsRemoved"/> (ADR-0054/A1; there is no separate delete event).
/// </summary>
public sealed record ProjectUpdated(ProjectPayload Payload) : WaldurIntegrationEvent;