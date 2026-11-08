namespace UserOrg.Shared.Persistance.Abstractions.Interfaces;

/// <summary>
/// Represents an entity that is associated with an external service (WALDUR, etc..), providing an identifier for integration purposes.
/// </summary>
/// <remarks>This interface is intended for use in scenarios where entities need to be linked to external systems.
/// Implementing classes should ensure that the ExternalId is unique across the external service.</remarks>
public interface IExtendedServiceIntegrationEntity : IServiceIntegrationEntity
{
    string? ExternalId { get; set; }
}