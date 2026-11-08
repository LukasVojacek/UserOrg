using Core.Domain.Shared;

using UserOrg.Shared;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>Optional filters for listing projects; null members are ignored.</summary>
/// <remarks>
/// <see cref="ScopeShortNames"/> restricts results to the given projects (caller scope, taken from
/// the internal token's <c>projectRoles</c> claim): <c>null</c> = no scoping, an empty list = no
/// projects match. <see cref="ProviderName"/> is the provider-admin tier (mirrors
/// <see cref="LocationFilter.ProviderName"/>): a project matches when at least one non-deleted
/// <c>ProjectResource</c> linked to it carries this provider, regardless of that link's validity.
/// </remarks>
public sealed record ProjectFilter(
    ProjectShortName? ShortName,
    Email? ContactEmail,
    DomainTypeName? DomainTypeName,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    IReadOnlyList<ProjectShortName>? ScopeShortNames = null,
    ProviderName? ProviderName = null);

public sealed record ProjectSummary(
    ProjectShortName ShortName,
    ProjectName Name,
    Email? Email,
    ExternalId? ExternalId,
    EOperationMode Mode,
    EProjectState State,
    ServiceName ServiceName,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);

/// <summary>Full read model for a single project, backing the <c>GetProject</c> endpoint.</summary>
public sealed record ProjectDetails(
    ProjectShortName ShortName,
    ProjectName Name,
    Description? Description,
    Email? Email,
    ExternalId? ExternalId,
    EOperationMode Mode,
    EProjectState State,
    ServiceName ServiceName,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    DomainTypeName DomainType,
    bool IsDeleted,
    IReadOnlyList<TagName> Tags,
    IReadOnlyList<ProjectUser> Users,
    IReadOnlyList<ResourceDetail> Resources);

public sealed record ProjectUserSummary(IdpId IdentityProviderId, Email Email, UserName UserName, ProjectRoleId RoleId);

public sealed record ExtendedProjectSummary(
    ProjectShortName ShortName,
    ProjectName Name,
    Description? Description,
    Email? Email,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    DomainTypeName DomainTypeName,
    IReadOnlyList<TagName> AssignedTags,
    IReadOnlyList<AssignedResourceData> AssignedResources);

public sealed record ProjectHeappeLinkEntry(
    Guid ProjectId,
    Uri? HeappeApi,
    HeappeIdentifier HeappeIdentifier,
    Guid ResourceId,
    string ResourceName,
    string LocationName,
    string LocationId,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);

public sealed record AssignedResourceData(
    string ResourceName,
    string? ResourceDescription,
    string LocationName,
    ELocationType LocationType,
    long? AllocationAmount,
    EAllocationUnitType AllocationUnitType,
    string? AggregationName,
    IReadOnlyList<(string Name, string Value)> Specifications);