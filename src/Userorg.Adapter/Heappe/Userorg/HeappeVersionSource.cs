using Heappe.HostContracts;

using UserOrg.Shared;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Userorg.Adapter.Heappe.Userorg;

/// <summary>
/// Resolves HEAppE endpoint versions straight from the HEAppE integration service.
/// </summary>
/// <remarks>
/// Split out of <see cref="UserorgService"/>: that type also serves the kill-switch's name
/// resolution and therefore depends on Core's project/location/user services, which in turn depend
/// (via the HEAppE connection service) back on the version resolver. Sharing one implementation
/// closed a DI cycle and stopped the API from starting. This type deliberately keeps its single
/// dependency, mirroring what the version logic actually needs.
/// </remarks>
/// <param name="heappeIntegrationService">The integration service used to communicate with the HEAppE system. Cannot be null.</param>
internal sealed class HeappeVersionSource(IHeappeIntegrationService heappeIntegrationService)
    : IHeappeVersionSource
{
    private static readonly Version _fallbackVersion = new(5, 0, 0);

    /// <inheritdoc />
    public async Task<Version> GetHeappeEndpointVersions(string projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);

        var shortName = ProjectShortName.Create(projectShortName);
        var heappeInstanceInfo = await heappeIntegrationService.GetHeappeEndpointVersions(shortName, identifier, cancellationToken);

        return heappeInstanceInfo is not null
             ? heappeInstanceInfo.Version
             : _fallbackVersion;
    }

    /// <inheritdoc />
    public async Task<Version> GetHeappeEndpointVersions(HeappeIdentifier identifier, CancellationToken cancellationToken)
    {
        var heappeInstanceInfo = await heappeIntegrationService.GetHeappeEndpointVersions(identifier, cancellationToken);

        return heappeInstanceInfo is not null
             ? heappeInstanceInfo.Version
             : _fallbackVersion;
    }
}