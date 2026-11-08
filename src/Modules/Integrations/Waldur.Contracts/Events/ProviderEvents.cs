using Waldur.Contracts.Payloads;

namespace Waldur.Contracts.Events;

/// <summary>Emitted when a provider (customer) id is not yet in the SHA map.</summary>
public sealed record ProviderCreated(ProviderPayload Payload) : WaldurIntegrationEvent;

/// <summary>Emitted when a known provider's payload hash differs from the SHA map.</summary>
public sealed record ProviderUpdated(ProviderPayload Payload) : WaldurIntegrationEvent;