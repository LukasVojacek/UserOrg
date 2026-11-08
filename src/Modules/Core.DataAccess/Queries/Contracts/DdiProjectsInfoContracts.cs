using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// All active project-resources owned by one provider, projected for the DDI "ProjectsInfo" aggregation
/// (legacy <c>ProjectInfoModel</c>). Resources are grouped by their own owning provider (ADR-0031 shadow
/// FK <c>ProviderId</c>); resources with no owning provider land in the empty-provider group
/// (<see cref="ProviderId"/> = <c>""</c>, <see cref="ProviderName"/> = <c>null</c>,
/// <see cref="ProviderDescription"/> = <c>""</c>), mirroring legacy <c>providerGroup.Key?.ToString()</c>.
/// </summary>
public sealed record ProviderResourcesView(
    string ProviderId,
    string? ProviderName,
    string ProviderDescription,
    IReadOnlyList<ProjectResourceInfoView> Resources);

/// <summary>
/// One active resource with its associated projects and location assignments
/// (legacy <c>ProjectResourceServiceModel</c>).
/// </summary>
public sealed record ProjectResourceInfoView(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    IReadOnlyList<AssociatedProjectView> AssociatedProjects,
    IReadOnlyList<ResourceAssignmentInfoView> Assignments);

/// <summary>
/// A project the resource is actively linked to (legacy <c>AssociatedProjectModel</c>). The
/// <see cref="CollectorName"/> reproduces the legacy collector key
/// <c>{ShortName}|{ProviderName-with-spaces-as-underscores}|{ResourceName}</c>.
/// </summary>
public sealed record AssociatedProjectView(
    Guid Id,
    string ProjectName,
    string ShortName,
    string CollectorName,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate);

/// <summary>
/// A resource's allocation at one location (legacy <c>ProjectResourceAssignmentServiceModel</c>).
/// <see cref="ResourceType"/> is the location's <c>LocationType.Name</c>.
/// </summary>
public sealed record ResourceAssignmentInfoView(
    string? LocationName,
    string? LocationDescription,
    string? ResourceType,
    long? AllocationAmount,
    EAllocationUnitType AllocationUnitType,
    string? AggregationName,
    IReadOnlyList<SpecificationView> Specifications);

public sealed record SpecificationView(string Name, string Value);