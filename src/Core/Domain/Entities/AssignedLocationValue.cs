using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class AssignedLocationValue : PrimaryIdentifiableEntity<Guid>
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public AssignedLocation? AssignedLocation { get; set; }
}