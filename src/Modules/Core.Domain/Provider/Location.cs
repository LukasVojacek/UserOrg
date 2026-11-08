using System.Collections.Immutable;
using System.Text.Json.Serialization;

using Core.Domain.Shared;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Provider;

/// <summary>
/// Location — child entity of the Provider aggregate.
/// Represents an HPC compute endpoint (cluster location), not a geographic address.
/// Id, ProviderId, ServiceName, DefaultServiceIntegrationUrl, CreatedAt and ModifiedAt are EF shadow
/// properties managed in <c>LocationConfiguration</c>; <see cref="LocationType"/> is mapped over the
/// LocationTypeId column; <see cref="Mode"/> is database-defaulted on insert.
/// </summary>
public sealed class Location : BaseAuditableEntity, ISoftDeletableEntity
{
    private readonly ICollection<AssignedLocation> _locationAllocations = [];

    public LocationName Name { get; private set; }
    public string? Description { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public bool IsPrivate { get; private set; }
    public bool IsShared { get; private set; }
    public bool IsDeleted { get; private set; }
    public ELocationType LocationType { get; private set; }

    /// <summary>Operation mode; read for the read-only guard. Persisted via the DB default on insert.</summary>
    public EOperationMode Mode { get; private set; }

    /// <summary>Resource allocations to this location (active only).</summary>
    public ImmutableList<AssignedLocation> AssignedLocations => [.. _locationAllocations];

    /// <param name="mode">In-memory only — persistence keeps the DB default on insert; used by tests
    /// to exercise the read-only guard without mocks.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="locationType"/> is
    /// <see cref="ELocationType.Unspecified"/> or not a defined value.</exception>
    public static Location Create(LocationName name, string endpoint, ELocationType locationType, bool isPrivate = false, bool isShared = true, string? description = null, EOperationMode mode = EOperationMode.Unspecified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (locationType == ELocationType.Unspecified || !Enum.IsDefined(locationType))
        {
            throw new ArgumentOutOfRangeException(nameof(locationType), locationType, "Location type must be a defined, specified value.");
        }

        return new Location
        {
            Name = name,
            Endpoint = endpoint,
            LocationType = locationType,
            IsPrivate = isPrivate,
            IsShared = isShared,
            Description = description,
            Mode = mode,
            IsDeleted = false,
        };
    }

    public void UpdateDetails(string endpoint, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        Endpoint = endpoint;
        Description = description;
    }

    public void Delete() => IsDeleted = true;

    /// <summary>
    /// Assigns a project resource to this location (creates a new <see cref="AssignedLocation"/>).
    /// Enforces the location-local invariant — a non-shared location may hold at most one active
    /// assignment — and the location read-only guard. The cross-aggregate invariant (the resource must
    /// not already hold a non-shared location) is checked by the use-case and passed in as
    /// <paramref name="resourceHasNonSharedLocation"/>.
    /// </summary>
    /// <param name="mode">In-memory only — persistence keeps the DB default on insert; used by tests
    /// to exercise the assignment read-only guard without mocks.</param>
    public Result<AssignedLocation, Error> AssignResource(
        ProjectResource resource,
        int allocationUnitTypeId,
        long? allocationUnitCount,
        string? description,
        string? aggregationName,
        IReadOnlyCollection<(string Name, string Value)> specifications,
        bool resourceHasNonSharedLocation,
        EOperationMode mode = EOperationMode.Unspecified,
        string? slug = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(specifications);

        if (Mode == EOperationMode.ReadOnly)
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.LocationReadOnly(Name));
        }

        if (resourceHasNonSharedLocation)
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.ResourceAlreadyHasNonSharedLocation(resource.ResourceName));
        }

        if (!IsShared && _locationAllocations.Any(a => !a.IsDeleted))
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.NonSharedLocationAlreadyHasResource(Name));
        }

        var assignment = AssignedLocation.Create(resource, allocationUnitTypeId, allocationUnitCount, description, aggregationName, mode, slug);
        assignment.ReplaceSpecifications(specifications);
        _locationAllocations.Add(assignment);
        return Result<AssignedLocation, Error>.Success(assignment);
    }

    /// <summary>
    /// Updates the details and replaces the specifications of the assignment with the given slug.
    /// Invariants are not re-checked on update (legacy parity) — the allocated resource and the
    /// location never change; only details and specifications do.
    /// </summary>
    public Result<AssignedLocation, Error> UpdateAssignment(
        string slug,
        int allocationUnitTypeId,
        long? allocationUnitCount,
        string? description,
        string? aggregationName,
        IReadOnlyCollection<(string Name, string Value)> specifications)
    {
        ArgumentNullException.ThrowIfNull(specifications);

        var result = FindActiveAssignment(slug);
        if (!result.IsSuccess)
        {
            return result;
        }

        var assignment = result.Value;
        assignment.UpdateDetails(allocationUnitTypeId, allocationUnitCount, description, aggregationName);
        assignment.ReplaceSpecifications(specifications);
        return result;
    }

    /// <summary>
    /// Soft-deletes the assignment with the given slug.
    /// </summary>
    public Result<AssignedLocation, Error> RemoveAssignment(string slug)
    {
        var result = FindActiveAssignment(slug);
        if (!result.IsSuccess)
        {
            return result;
        }

        result.Value.Delete();
        return result;
    }

    // Addressed by the v2 slug (ADR-0024): a v1-created allocation has a null Slug, so it never matches a
    // route slug and yields the same not-found as a genuine miss.
    private Result<AssignedLocation, Error> FindActiveAssignment(string slug)
    {
        var assignment = _locationAllocations.FirstOrDefault(a => a.Slug == slug);
        if (assignment is null)
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.AssignedLocationNotFound(Name));
        }

        if (assignment.IsDeleted)
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.AssignedLocationAlreadyDeleted(Name));
        }

        if (assignment.Mode == EOperationMode.ReadOnly)
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.AssignmentReadOnly(Name));
        }

        return Result<AssignedLocation, Error>.Success(assignment);
    }
}