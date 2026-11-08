using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace UserOrg.Shared.Persistance.Abstractions.Interfaces;

/// <summary>
/// Defines the contract for entities that participate in service integration operations, allowing configuration of the
/// operation mode and associated service name.
/// </summary>
/// <remarks>Implementations of this interface represent entities that interact with external or internal
/// services. The interface enables specifying the operational context and identifying the target service, which is
/// essential for service-oriented workflows or integrations.</remarks>
public interface IServiceIntegrationEntity
{
    EOperationMode Mode { get; set; }
    string ServiceName { get; set; }
}