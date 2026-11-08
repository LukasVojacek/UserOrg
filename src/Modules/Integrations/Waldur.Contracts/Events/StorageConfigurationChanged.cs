using Waldur.Contracts.Payloads;

namespace Waldur.Contracts.Events;

/// <summary>
/// Emitted when the EFP storage-reconciliation config hash changes, and once unconditionally after
/// startup (ADR-0061). Drives the full storage reconcile on the consumer side.
/// </summary>
public sealed record StorageConfigurationChanged(StorageConfigPayload Config) : WaldurIntegrationEvent;