using Core.Domain.Shared;

namespace Core.Domain.Provider;

/// <summary>
/// The new resource's own data, supplied to <see cref="IResourceProvisioningService.Provision"/>.
/// <see cref="Name"/> is the resource's natural-name part of its <see cref="ProjectResourceKey"/>;
/// the owning provider is the resolved aggregate passed alongside it.
/// </summary>
public sealed record NewResourceSpec(
    ResourceName Name,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string? Description,
    Email? PrimaryInvestigatorEmail,
    string? LicenseUrl);