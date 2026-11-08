using UserOrg.Shared;

namespace Heappe.HostContracts;

/// <summary>
/// Supplies the version of a HEAppE endpoint.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IUserorgService"/>, which is a broad host contract whose
/// implementation reaches into Core's project/location/user services. The version resolver sits
/// underneath those services, so depending on the broad contract closed a DI cycle
/// (IProjectService -> ... -> IUserorgService -> IProjectService) and the API failed to start.
/// Keeping this contract narrow keeps the resolver's dependencies below that layer.
/// </remarks>
public interface IHeappeVersionSource
{
    /// <summary>
    /// Gets the version of a HEAppE endpoint as configured for a specific project.
    /// </summary>
    /// <param name="projectShortName">Short name of the project the endpoint is resolved for.</param>
    /// <param name="identifier">Identifier of the HEAppE instance.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The endpoint's version, or a fallback version when it cannot be determined.</returns>
    Task<Version> GetHeappeEndpointVersions(string projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the version of a HEAppE endpoint.
    /// </summary>
    /// <param name="identifier">Identifier of the HEAppE instance.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The endpoint's version, or a fallback version when it cannot be determined.</returns>
    Task<Version> GetHeappeEndpointVersions(HeappeIdentifier identifier, CancellationToken cancellationToken);
}