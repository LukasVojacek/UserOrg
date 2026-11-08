using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Contracts.Entries;
using Heappe.Contracts.Entries.Sync;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;


namespace Heappe.HostContracts;

/// <summary>
/// Defines methods for retrieving user project allocation information and HEAppE endpoint version details.
/// </summary>
/// <remarks>Implementations of this interface provide access to user allocation data and HEAppE endpoint
/// versioning, which may be used to manage user permissions and ensure compatibility with HEAppE services. Methods are
/// asynchronous and support cancellation via a CancellationToken.</remarks>
public interface IUserorgService
{
    Task<Result<IList<ProjectAllocationInfoEntry>, Error>> GetUserProjectAllocationsAsync(string? idpUserId, string? heappeIdentifier, CancellationToken ct);

    Task<Result<ProjectResourceInfoEntry, Error>> GetProjectResourceAllocationAsync(string projectShortName, string resourceName, CancellationToken ct);
    Task<Result<ProjectResourceInfoSyncEntry, Error>> GetProjectResourceAllocationWithLocationsAsync(string projectShortName, string resourceName, CancellationToken ct);


    /// <summary>
    /// Retrieves valid location info for the specified project resource and location types.
    /// Returns a Result containing a set of distinct ProviderLocationInfo records; a location can appear multiple times with different aggregation names.
    /// Empty set indicates no valid locations found for the given project resource and location types.
    /// Failure result indicates invalid inputs or system errors (e.g., invalid project/resource/locationTypeIds).
    /// This method is used to obtain the valid locations that a project resource can be associated with.
    /// </summary>
    /// <param name="projectShortName">The short name of the project. Must be valid.</param>
    /// <param name="resourceName">The name of the resource. Must be valid.</param>
    /// <param name="locationTypeIds">Location type IDs based on Domain.ELocationType.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<HashSet<ProviderLocationInfo>, Error>> GetValidLocationNames(
        string projectShortName,
        string resourceName,
        IEnumerable<int> locationTypeIds,
        CancellationToken cancellationToken);

    // KillSwitch host-contract resolution (ADR-0082 §5, relationship-map §D)

    /// <summary>
    /// Resolves a provider's internal id from its display name using an exact, case-insensitive match.
    /// </summary>
    /// <remarks><c>Provider.Name</c> has no unique index; an ambiguous (more than one) or missing match returns a
    /// failure Result so the caller can fail closed (the KillSwitch endpoints map this to 409) instead of guessing
    /// which provider was meant.</remarks>
    /// <param name="providerName">The provider's display name to resolve. Cannot be null or whitespace.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A Result containing the matched provider's id, or a failure if the name is missing or ambiguous.</returns>
    Task<Result<Guid, Error>> ResolveProviderIdByNameAsync(string providerName, CancellationToken ct);

    /// <summary>
    /// Retrieves the ids of a provider's current, non-deleted locations.
    /// </summary>
    /// <param name="providerId">The id of the provider whose location ids are requested.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A Result containing the provider's live location ids.</returns>
    Task<Result<IList<Guid>, Error>> GetLocationIdsForProviderAsync(Guid providerId, CancellationToken ct);

    /// <summary>
    /// Resolves a batch of <c>(ProjectShortName, ResourceName)</c> pairs to their internal
    /// <c>ProjectId</c>/<c>ProjectResourceId</c> in two Core round trips, regardless of batch size.
    /// </summary>
    /// <remarks><para>A pair is <b>omitted</b> from the result when either name is unknown to Core, when a name is
    /// ambiguous (matches more than one row), or when the resource is <b>not assigned to that project</b> —
    /// ownership is enforced here. Resolution is fail-closed and never guesses
    /// a winner. Matching is case-insensitive and each entry carries Core's spelling of both names, not the
    /// caller's.</para>
    /// <para>Callers needing atomicity diff the returned entries against the pairs they asked for. The diff must
    /// be case-insensitive and must not be positional — omissions shift positions, so entries align with the
    /// resolvable subset, not with the request.</para></remarks>
    /// <param name="pairs">The name pairs to resolve. Cannot be null; an empty collection resolves to an empty result without calling Core.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A Result containing one entry per resolvable pair, or a failure if the batch could not be resolved at all.</returns>
    Task<Result<IList<ProjectResourceNameEntry>, Error>> ResolveProjectResourceIdsAsync(
        IReadOnlyCollection<ProjectResourceNamePair> pairs,
        CancellationToken ct);

    /// <summary>
    /// Retrieves every current location together with the provider that owns it.
    /// </summary>
    /// <remarks>The kill-switch admin surface speaks provider/location names while the module stores location
    /// ids, and neither name has a unique index — so resolution happens against this list, which lets the caller
    /// detect an ambiguous match and fail closed (409) rather than guess. A cold admin path, not a hot read path.</remarks>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A Result containing the live locations with their owning provider's id and name; returns an error on system failure.</returns>
    Task<Result<IList<ProviderLocationEntry>, Error>> GetProviderLocationsAsync(CancellationToken ct);

    /// <summary>
    /// Resolves an identity-provider user id to the internal user id.
    /// </summary>
    /// <remarks>Claims carry only the identity-provider's subject; the kill-switch audit columns
    /// (<c>EnforcedByUserId</c>/<c>LiftedByUserId</c>) record the internal id.</remarks>
    /// <param name="identityProviderUserId">The subject claim value assigned by the identity provider. Cannot be null or whitespace.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A Result containing the internal user id, or a failure when no such user exists.</returns>
    Task<Result<Guid, Error>> ResolveUserIdByIdentityProviderIdAsync(string identityProviderUserId, CancellationToken ct);

    /// <summary>
    /// Resolves internal <c>(ProjectId, ProjectResourceId)</c> pairs to the names Core knows them by.
    /// </summary>
    /// <remarks>The inverse of <see cref="ResolveProjectResourceIdsAsync"/>. A Heappe partition carries only
    /// ids, but the admin responses and the resync queue are name-based; pairs that no longer resolve are omitted
    /// rather than failing the whole batch.</remarks>
    /// <param name="projectResourceIds">The id pairs to resolve. Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A Result containing an entry for each pair that resolved; returns an error on system failure.</returns>
    Task<Result<IList<ProjectResourceNameEntry>, Error>> ResolveProjectResourceNamesAsync(
        IReadOnlyCollection<ProjectResourceIdEntry> projectResourceIds,
        CancellationToken ct);
}