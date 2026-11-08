using Heappe.Contracts;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Userorg.Adapter.Heappe;

/// <summary>
/// This class serves as a port for triggering synchronization of project resources with the Heappe system. It implements the IHeappeSynchronizationPort interface and utilizes the IHeappeSynchronizationService to perform the actual synchronization logic.
/// </summary>
/// <param name="heappeSynchronizationService"></param>
internal class HeappeSynchronizationPort(IHeappeSynchronizationService heappeSynchronizationService) : IHeappeSynchronizationPort
{
    /// <summary>
    /// Triggers the synchronization of a specific project resource with the Heappe system. This method takes the project's short name and the resource name as parameters, and it uses the IHeappeSynchronizationService to initiate the synchronization process. The operation can be canceled using the provided CancellationToken.
    /// </summary>
    /// <param name="projectShortName"></param>
    /// <param name="resourceName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task TriggerSynchronizationForProjectResource(string projectShortName, string resourceName, CancellationToken cancellationToken)
    {
        await heappeSynchronizationService.TriggerSynchronizationForProjectResource(new ProjectShortName(projectShortName), resourceName, cancellationToken);
    }
}