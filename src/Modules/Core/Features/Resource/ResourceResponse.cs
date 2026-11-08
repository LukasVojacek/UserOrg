using Core.Domain.Shared;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.Features.Resource;

internal record ResourceResponse(
    string Name,
    string? ProviderResourceName,
    string? Description,
    string? PrimaryInvestigatorEmail,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string OperationMode,
    string? LicenseUrl,
    EProjectResourceState State,
    IReadOnlyList<ResourceAssignmentResponse> Assignments)
{
    internal static ResourceResponse From(ResourceSummary r) => new(
        r.Name,
        r.ProviderResourceName,
        r.Description,
        r.PrimaryInvestigatorEmail,
        r.StartDate,
        r.EndDate,
        r.OperationMode.ToString(),
        r.LicenseUrl,
        r.EProjectResourceState,
        r.Assignments.Select(ResourceAssignmentResponse.From).ToList());
}

internal sealed record ResourceAssignmentResponse(
    string? Slug,
    ELocationType LocationType,
    bool IsLocationShared,
    string LocationName,
    long? AllocationAmount,
    EAllocationUnitType AllocationUnitType,
    string? AggregationName,
    string OperationMode,
    IReadOnlyList<SpecificationResponse> Specifications)
{
    // Addressed by the GUID-free Slug (ADR-0024); the location is identified by its name. v1-created
    // allocations have a null slug (handled on the legacy path).
    internal static ResourceAssignmentResponse From(ResourceAssignmentSummary a) => new(
        a.Slug,
        a.LocationType,
        a.IsLocationShared,
        a.LocationName,
        a.AllocationAmount,
        a.AllocationUnitType,
        a.AggregationName,
        a.OperationMode.ToString(),
        a.Specifications.Select(s => new SpecificationResponse(s.Name, s.Value)).ToList());
}

internal sealed record SpecificationResponse(string Name, string Value);