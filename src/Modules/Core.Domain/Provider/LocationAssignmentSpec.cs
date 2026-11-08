namespace Core.Domain.Provider;

/// <summary>
/// One entry of a multi-location <see cref="IResourceProvisioningService.Provision"/> batch: the location the
/// new resource is allocated to, paired with that location's own allocation values.
/// </summary>
public sealed record LocationAssignmentSpec(LocationName LocationName, ResourceAllocationSpec Allocation);