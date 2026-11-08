using Core.Domain.Shared;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Filters for listing resources, expressing the read-scope tier (see CONTEXT.md "Read scope tier"):
/// <list type="bullet">
/// <item><see cref="ScopeShortNames"/> non-null → caller (user) tier: only resources assigned to a
/// project in that scope (the project short-names from the <c>projectRoles</c> claim).</item>
/// <item><see cref="ScopeProviderName"/> non-null → provider tier: also the resources assigned to a
/// location of that provider (regardless of project attachment). Set together with
/// <see cref="ScopeShortNames"/> the two are a union — the provider tier adds to the caller's reach,
/// it never replaces it.</item>
/// <item>else (both null) → admin tier: every resource.</item>
/// </list>
/// Other members are optional search filters; null members are ignored. <see cref="FilterProviderName"/>
/// narrows the result to a single provider (resources with an active assignment on one of its locations)
/// while keeping the caller's read-scope tier — it is the path scope of the provider-nested resource routes
/// (<c>Providers/{providerName}/Resources</c>), not a tier of its own.
/// </summary>
public sealed record ResourceFilter(
    IReadOnlyList<ProjectShortName>? ScopeShortNames,
    ProjectShortName? ProjectShortName,
    ELocationType? LocationType,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    ProviderName? ScopeProviderName = null,
    ProviderName? FilterProviderName = null);

/// <summary>
/// Read DTO for a resource (legacy <c>ProjectResource</c>) and its location assignments. Mirrors the
/// legacy <c>ProjectResourceModel</c> projection.
/// </summary>
public sealed record ResourceSummary(
    string Name,
    string? ProviderResourceName,
    string? Description,
    string? PrimaryInvestigatorEmail,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    EOperationMode OperationMode,
    string? LicenseUrl,
    EProjectResourceState EProjectResourceState,
    Email? CreatedByEmail,
    IReadOnlyList<ResourceAssignmentSummary> Assignments);

/// <summary>
/// A resource's allocation at one location (legacy <c>AssignedLocation</c> /
/// <c>ProjectResourceAssignmentModel</c>). Addressed by its GUID-free <see cref="Slug"/> (ADR-0024) —
/// null for v1-created allocations; the surrogate id and the resource/location FKs are no longer exposed,
/// the allocation is identified by its slug and the location by its <see cref="LocationName"/>.
/// </summary>
public sealed record ResourceAssignmentSummary(
    string? Slug,
    ELocationType LocationType,
    bool IsLocationShared,
    string LocationName,
    long? AllocationAmount,
    EAllocationUnitType AllocationUnitType,
    string? AggregationName,
    EOperationMode OperationMode,
    IReadOnlyList<SpecificationSummary> Specifications);

public sealed record SpecificationSummary(string Name, string Value);