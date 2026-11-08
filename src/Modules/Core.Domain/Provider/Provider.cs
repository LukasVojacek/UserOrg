using System.Collections.Immutable;
using System.Text.Json.Serialization;

using Core.Domain.Shared;

using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Provider;

/// <summary>
/// Provider Aggregate Root (natural identifier: <see cref="Name"/>).
///
/// Pure domain model — Id, CreatedAt and ModifiedAt are EF shadow properties; the
/// <c>CountryId</c> lookup FK is mapped in <c>ProviderConfiguration</c>.
/// The <c>_locations</c> backing field is mapped by EF; <see cref="Locations"/> exposes a read-only view.
/// </summary>
public sealed class Provider : AggregateRoot, ISoftDeletableEntity, IServiceIntegrationEntity
{
    private readonly ICollection<Location> _providerLocations = [];

    /// <summary>Natural identifier — provider name.</summary>
    public ProviderName Name { get; private set; }

    /// <summary>Provider description. Null when not set (the column is nullable in the DB).</summary>
    public Description? Description { get; private set; }

    /// <summary>Contact/support email for the provider. Null when not set (column is nullable).</summary>
    public Email? SupportEmail { get; private set; }

    /// <summary>External identifier from an integrated service. Null when not assigned.</summary>
    public ExternalId? ExternalId { get; private set; }

    public EOperationMode Mode { get; private set; }

    public ServiceName ServiceName { get; private set; }

    public bool IsPrivate { get; private set; }

    /// <summary>Country lookup reference (FK to the Country reference table).</summary>
    public int CountryId { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>Locations offered by the provider.</summary>
    public ImmutableList<Location> Locations => [.. _providerLocations];

    // EF materialization + factory only; closes the implicit public parameterless constructor so
    // `new Provider()` cannot bypass Create.
    private Provider() { }

    public static Provider Create(
        ProviderName name,
        int countryId,
        Description? description = null,
        Email? supportEmail = null,
        EOperationMode mode = EOperationMode.Unspecified,
        bool isPrivate = false,
        ServiceName? serviceName = null,
        ExternalId? externalId = null)
    {
        var provider = new Provider
        {
            Name = name,
            CountryId = countryId,
            Description = description,
            SupportEmail = supportEmail,
            Mode = mode,
            IsPrivate = isPrivate,
            ServiceName = serviceName ?? ServiceName.Default,
            ExternalId = externalId,
            IsDeleted = false,
        };

        provider.AddDomainEvent(new ProviderCreatedEvent(name));
        return provider;
    }

    // A read-only provider (synced from an external system) rejects all root-level edits, mirroring the
    // guard its Location/AssignedLocation children already enforce and legacy ThrowIfReadOnly(Mode).
    private Error? EnsureNotReadOnly() =>
        Mode == EOperationMode.ReadOnly ? ProviderErrors.ProviderIsReadOnly(Name) : null;

    public Result<Error> UpdateDetails(Description? description, Email? supportEmail)
    {
        if (EnsureNotReadOnly() is { } error)
            return Result<Error>.Failure(error);

        Description = description;
        SupportEmail = supportEmail;
        return Result<Error>.Success();
    }

    public Result<Error> SetPrivacy(bool isPrivate)
    {
        if (EnsureNotReadOnly() is { } error)
            return Result<Error>.Failure(error);

        IsPrivate = isPrivate;
        return Result<Error>.Success();
    }

    /// <summary>
    /// Re-points the provider at a different country lookup row. A plain FK set with no event and no
    /// validation — country existence is verified by the use-case before this is called (it resolves
    /// the supplied code to a <c>CountryId</c>), mirroring <see cref="UpdateDetails"/>/<see cref="SetPrivacy"/>.
    /// </summary>
    public Result<Error> ChangeCountry(int countryId)
    {
        if (EnsureNotReadOnly() is { } error)
            return Result<Error>.Failure(error);

        CountryId = countryId;
        return Result<Error>.Success();
    }

    public Result<Error> Deactivate()
    {
        if (IsDeleted)
        {
            return Result<Error>.Failure(ProviderErrors.AlreadyDeactivated(Name));
        }

        IsDeleted = true;
        AddDomainEvent(new ProviderDeletedEvent(Name));
        return Result<Error>.Success();
    }

    public Result<Error> Reactivate()
    {
        if (!IsDeleted)
        {
            return Result<Error>.Failure(ProviderErrors.NotDeactivated(Name));
        }

        IsDeleted = false;
        return Result<Error>.Success();
    }

    /// <summary>
    /// Creates and registers a new location under this provider.
    /// Returns an error if an active location with the same name already exists.
    /// Resource allocations are assigned separately via <see cref="AssignResource"/> — a location
    /// starts with none (legacy parity; an allocation requires an existing ProjectResource).
    /// </summary>
    public Result<Error> AddLocation(
        LocationName name,
        string endpoint,
        ELocationType locationType,
        bool isPrivate = false,
        bool isShared = true,
        string? description = null)
    {
        if (_providerLocations.Any(l => !l.IsDeleted && l.Name.Equals(name)))
        {
            return Result<Error>.Failure(ProviderErrors.LocationAlreadyExists(name, Name));
        }

        var location = Location.Create(name, endpoint, locationType, isPrivate, isShared, description);
        _providerLocations.Add(location);
        AddDomainEvent(new LocationAddedEvent(Name, name));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Soft-deletes the named location.
    /// Returns an error if the location is not found or is already deleted.
    /// </summary>
    public Result<Error> RemoveLocation(LocationName name)
    {
        var location = _providerLocations.FirstOrDefault(l => l.Name.Equals(name));
        if (location is null)
        {
            return Result<Error>.Failure(ProviderErrors.LocationNotFound(name, Name));
        }

        if (location.IsDeleted)
        {
            return Result<Error>.Failure(ProviderErrors.LocationAlreadyDeleted(name));
        }

        location.Delete();
        AddDomainEvent(new LocationRemovedEvent(Name, name));
        return Result<Error>.Success();
    }

    /// <summary>
    /// Updates the endpoint and description of the named location.
    /// Returns an error if the location is not found or is already deleted.
    /// </summary>
    public Result<Error> UpdateLocation(LocationName name, string endpoint, string? description)
    {
        var location = _providerLocations.FirstOrDefault(l => l.Name.Equals(name));
        if (location is null)
        {
            return Result<Error>.Failure(ProviderErrors.LocationNotFound(name, Name));
        }

        if (location.IsDeleted)
        {
            return Result<Error>.Failure(ProviderErrors.LocationAlreadyDeleted(name));
        }

        location.UpdateDetails(endpoint, description);
        return Result<Error>.Success();
    }

    public Result<Error> AddProjectResource(ResourceName name, string? description, Email emailPI, DateTimeOffset startDate, DateTimeOffset endDate, LocationName locationName)
    {

        return Result<Error>.Success();
    }

    // Thin root-level wrappers over the named location: the local rules live on Location;
    // the root resolves the location and raises ResourceAssignmentChangedEvent on success
    // (domain events can only be raised from the aggregate root).

    /// <summary>
    /// Assigns a project resource to the named location. See <see cref="Location.AssignResource"/>
    /// for the location-local invariants. Enforces invariant #5 in-aggregate (ADR-0044 L2.3): the resource
    /// must belong to this provider — its <c>ProviderName</c> must equal this provider's <see cref="Name"/>.
    /// Returns the created <see cref="AssignedLocation"/>.
    /// </summary>
    public Result<AssignedLocation, Error> AssignResource(
        LocationName locationName,
        ProjectResource resource,
        int allocationUnitTypeId,
        long? allocationUnitCount,
        string? description,
        string? aggregationName,
        IReadOnlyCollection<(string Name, string Value)> specifications,
        bool resourceHasNonSharedLocation,
        string? slug = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        // Invariant #5 (ADR-0026/0041 L2.3): a resource is assignable only under its OWN provider. With the
        // resolved entity in hand the check is on the natural key (ProviderName), not the surrogate ProviderId.
        if (resource.ProviderName != Name)
        {
            return Result<AssignedLocation, Error>.Failure(ProviderErrors.ResourceNotUnderProvider(resource.ResourceName, Name));
        }

        var locationResult = FindActiveLocation(locationName);
        if (!locationResult.IsSuccess)
        {
            return Result<AssignedLocation, Error>.Failure(locationResult.Error);
        }

        var result = locationResult.Value.AssignResource(
            resource, allocationUnitTypeId, allocationUnitCount, description, aggregationName,
            specifications, resourceHasNonSharedLocation, slug: slug);

        if (result.IsSuccess)
        {
            AddDomainEvent(new ResourceAssignmentChangedEvent(resource.Key));
        }

        return result;
    }

    /// <summary>
    /// Updates the assignment with the given slug on the named location (details + replace-all specifications).
    /// </summary>
    public Result<AssignedLocation, Error> UpdateAssignment(
        LocationName locationName,
        string slug,
        int allocationUnitTypeId,
        long? allocationUnitCount,
        string? description,
        string? aggregationName,
        IReadOnlyCollection<(string Name, string Value)> specifications)
    {
        var locationResult = FindActiveLocation(locationName);
        if (!locationResult.IsSuccess)
        {
            return Result<AssignedLocation, Error>.Failure(locationResult.Error);
        }

        var result = locationResult.Value.UpdateAssignment(
            slug, allocationUnitTypeId, allocationUnitCount, description, aggregationName, specifications);

        if (result.IsSuccess)
        {
            AddDomainEvent(new ResourceAssignmentChangedEvent(result.Value.ResourceKey!.Value));
        }

        return result;
    }

    /// <summary>
    /// Soft-deletes the assignment with the given slug on the named location.
    /// </summary>
    public Result<AssignedLocation, Error> RemoveAssignment(LocationName locationName, string slug)
    {
        var locationResult = FindActiveLocation(locationName);
        if (!locationResult.IsSuccess)
        {
            return Result<AssignedLocation, Error>.Failure(locationResult.Error);
        }

        var result = locationResult.Value.RemoveAssignment(slug);
        if (result.IsSuccess)
        {
            AddDomainEvent(new ResourceAssignmentChangedEvent(result.Value.ResourceKey!.Value));
        }

        return result;
    }

    private Result<Location, Error> FindActiveLocation(LocationName name)
    {
        var location = _providerLocations.FirstOrDefault(l => l.Name.Equals(name));
        if (location is null)
        {
            return Result<Location, Error>.Failure(ProviderErrors.LocationNotFound(name, Name));
        }

        return location.IsDeleted
            ? Result<Location, Error>.Failure(ProviderErrors.LocationAlreadyDeleted(name))
            : Result<Location, Error>.Success(location);
    }
}