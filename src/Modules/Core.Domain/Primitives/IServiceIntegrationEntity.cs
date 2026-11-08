using Core.Domain.Shared;

namespace Core.Domain.Primitives;

/// <summary>
/// Identifies entities that integrate with an external service and are tracked by an external identifier.
/// </summary>
public interface IServiceIntegrationEntity
{
    ExternalId? ExternalId { get; }
}