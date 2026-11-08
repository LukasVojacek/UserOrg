using Heappe.HostContracts;

using UserOrg.Shared;

namespace Heappe.Features.VersionResolver;

internal sealed class HeappeVersionDatabaseResolver(
    IHeappeVersionSource versionSource) : IHeappeVersionResolver
{
    public Task<Version> GetVersion(string projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken) =>
        versionSource.GetHeappeEndpointVersions(projectShortName, identifier, cancellationToken);

    public Task<Version> GetVersion(HeappeIdentifier identifier, CancellationToken cancellationToken) =>
        versionSource.GetHeappeEndpointVersions(identifier, cancellationToken);
}