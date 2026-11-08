using System.Collections.Immutable;

using Core.Domain.Shared;

namespace Core.Domain.Provider;

/// <summary>
/// AssignedLocation — child entity (within the Provider aggregate) mapping a resource allocation
/// to a location. Unlike its name-keyed parents it has no natural key — the schema's
/// (LocationId, ProjectResourceId) index is non-unique — so the surrogate <see cref="Id"/> is its
/// public identity (ADR-0016). The allocated resource is held as a <b>navigation</b> over the shadow FK
/// <c>ProjectResourceId</c> (ADR-0031/0041 L2.1) — the surrogate stays in storage and the domain surface
/// exposes only the resource's natural key via <see cref="ResourceKey"/>. The LocationId FK, ServiceName,
/// ExternalId, CreatedAt and ModifiedAt remain EF shadow properties; <see cref="Mode"/> is
/// database-defaulted on insert.
/// </summary>
public sealed class AssignedLocation : BaseAuditableEntity, ISoftDeletableEntity
{
    private readonly ICollection<AssignedLocationValue> _assignedLocationValues = [];

    // EF loads the allocated resource into this backing field through the shadow FK "ProjectResourceId"
    // (ADR-0031/0041); the surrogate stays in storage and never crosses a contract. Mirrors ProjectResource._provider.
    private ProjectResource? _resource;

    /// <summary>Surrogate identity (database-generated). Exposed because the entity has no natural key.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Cross-aggregate natural identity of the allocated resource (ADR-0044 L2.1) — read via the
    /// <c>_resource</c> navigation, never the surrogate. Populated only when the navigation is materialised.
    /// </summary>
    public ProjectResourceKey? ResourceKey => _resource?.Key;

    /// <summary>
    /// The allocated resource's operation mode (ADR-0044 L2.5) — read for the cross-aggregate read-only
    /// guard from the loaded navigation, replacing a separate id-keyed query. Null when the resource
    /// navigation is not materialised (e.g. the resource was soft-deleted, so the !IsDeleted Include yields
    /// no row) — the guard treats a null as "does not block", matching the legacy missing-resource rule.
    /// </summary>
    public EOperationMode? ResourceOperationMode => _resource?.OperationMode;

    /// <summary>Lookup FK to the seeded AllocationUnitType reference table.</summary>
    public int AllocationUnitTypeId { get; private set; }

    /// <summary>Operation mode; read for the read-only guard. Persisted via the DB default on insert.</summary>
    public EOperationMode Mode { get; private set; }

    public long? AllocationUnitCount { get; private set; }
    public string? Description { get; private set; }
    public string? AggregationName { get; private set; }

    /// <summary>
    /// GUID-free public identifier a v2 client addresses this allocation by (ADR-0024). Null for
    /// v1-created allocations (handled on the legacy path); v2 generates one via <see cref="AllocationSlug"/>.
    /// </summary>
    public string? Slug { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>Values (specifications) attached to this assignment.</summary>
    public ImmutableList<AssignedLocationValue> Values => [.. _assignedLocationValues];

    /// <param name="mode">In-memory only — persistence keeps the DB default on insert; used by tests
    /// to exercise the read-only guard without mocks.</param>
    public static AssignedLocation Create(
        ProjectResource resource,
        int allocationUnitTypeId,
        long? allocationUnitCount = null,
        string? description = null,
        string? aggregationName = null,
        EOperationMode mode = EOperationMode.Unspecified,
        string? slug = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new AssignedLocation
        {
            // Client-generated so the entity is addressable before save (EF inserts a non-default
            // value instead of the NewId() column default).
            Id = Guid.CreateVersion7(),
            // Set the navigation; EF derives the shadow FK "ProjectResourceId" from it (ADR-0031/0041).
            _resource = resource,
            AllocationUnitTypeId = allocationUnitTypeId,
            AllocationUnitCount = allocationUnitCount,
            Description = description,
            AggregationName = aggregationName,
            Mode = mode,
            Slug = slug,
            IsDeleted = false,
        };
    }

    public void UpdateDetails(int allocationUnitTypeId, long? allocationUnitCount, string? description, string? aggregationName)
    {
        AllocationUnitTypeId = allocationUnitTypeId;
        AllocationUnitCount = allocationUnitCount;
        Description = description;
        AggregationName = aggregationName;
    }

    /// <summary>
    /// Replaces all specifications with the supplied set (delete-all + insert, matching legacy).
    /// No uniqueness is enforced on names.
    /// </summary>
    public void ReplaceSpecifications(IReadOnlyCollection<(string Name, string Value)> specifications)
    {
        ArgumentNullException.ThrowIfNull(specifications);

        _assignedLocationValues.Clear();
        foreach (var (name, value) in specifications)
        {
            _assignedLocationValues.Add(AssignedLocationValue.Create(name, value));
        }
    }

    public void Delete() => IsDeleted = true;
}