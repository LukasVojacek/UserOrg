namespace Core.Domain.Project;

/// <summary>
/// Read DTO for a resource assigned to a project (the <see cref="Project.Resources"/> projection).
/// <see cref="ProviderName"/> is the owning provider's natural key (ADR-0044 §1); it is null only for
/// legacy standalone resources that carry no provider.
/// </summary>
public readonly record struct ResourceDetail(ResourceName Name, ProviderName? ProviderName, DateTimeOffset StartDate, DateTimeOffset EndDate);