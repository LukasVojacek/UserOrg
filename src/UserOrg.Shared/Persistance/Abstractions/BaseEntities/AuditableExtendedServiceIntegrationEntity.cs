using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrg.Shared.Persistance.Abstractions.BaseEntities;

/// <summary>
/// Represents an auditable entity that supports integration with extended service functionalities and provides a
/// structure for tracking external identifiers.
/// </summary>
/// <remarks>This class serves as a base for entities requiring auditing and extended service integration
/// capabilities. The ExternalId property enables linking to external systems or records, facilitating interoperability
/// and traceability across service boundaries.</remarks>
/// <typeparam name="T">The type of the entity's identifier. Must be a value type that implements the IEquatable interface.</typeparam>
public abstract class AuditableExtendedServiceIntegrationEntity<T> : AuditableServiceIntegrationEntity<T>, IExtendedServiceIntegrationEntity where T : struct, IEquatable<T>
{
    public string? ExternalId { get; set; }
}