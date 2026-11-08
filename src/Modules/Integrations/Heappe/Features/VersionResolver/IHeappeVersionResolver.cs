using UserOrg.Shared;

namespace Heappe.Features.VersionResolver;

public interface IHeappeVersionResolver
{
    Task<Version> GetVersion(string projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken);
    Task<Version> GetVersion(HeappeIdentifier identifier, CancellationToken cancellationToken);
}