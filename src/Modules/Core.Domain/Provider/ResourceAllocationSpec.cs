namespace Core.Domain.Provider;

/// <summary>
/// The allocation (<see cref="AssignedLocation"/>) data, supplied to
/// <see cref="IResourceProvisioningService.Provision"/> for the named location the new resource is allocated to.
/// </summary>
public sealed record ResourceAllocationSpec(
    int AllocationUnitTypeId,
    long? AllocationUnitCount,
    string? Description,
    string? AggregationName,
    IReadOnlyCollection<(string Name, string Value)> Specifications,
    string Slug);