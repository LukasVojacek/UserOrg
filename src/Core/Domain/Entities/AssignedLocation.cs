using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class AssignedLocation : AuditableExtendedServiceIntegrationEntity<Guid>
{
    public Guid ProjectResourceId { get; set; }
    public Guid LocationId { get; set; }
    public int AllocationUnitTypeId { get; set; }

    public ProjectResource? ProjectResource { get; set; }
    public Location? Location { get; set; }
    public long? AllocationUnitCount { get; set; }
    public AllocationUnitType? AllocationUnitType { get; set; }
    public IList<AssignedLocationValue> AssignedLocationsValues { get; init; } = [];
    public string? Description { get; set; }
    public string? AggregationName { get; set; }
    public string? Slug { get; set; }
}