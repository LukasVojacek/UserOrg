using UserOrg.Shared.Persistance.Abstractions.Enums;
using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrg.Shared.Persistance.Abstractions.BaseEntities;

/// <summary>
/// Represents an abstract base class for entities that require auditing and integration with external services,
/// providing common properties for operation mode and service name.
/// </summary>
/// <remarks>This class serves as a foundation for creating auditable entities that interact with external
/// services. It allows specifying the operation mode and the name of the integrated service. The default service name
/// is "Lexis", and the operation mode is set to "Unspecified".</remarks>
/// <typeparam name="T">The type of the identifier for the entity. Must be a value type that implements IEquatable<T>.</typeparam>
public abstract class AuditableServiceIntegrationEntity<T> : AuditableEntity<T>, IServiceIntegrationEntity where T : struct, IEquatable<T>
{
    public EOperationMode Mode { get; set; } = EOperationMode.Unspecified;
    public string ServiceName { get; set; } = "Lexis";
}