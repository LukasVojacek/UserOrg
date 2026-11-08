using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class Provider : AuditableExtendedServiceIntegrationEntity<Guid>
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsPrivate { get; set; }
    public string? SupportEmail { get; set; }
    public Country? Country { get; set; }
    public IList<Location> Locations { get; private set; } = [];
    public IList<User>? ResponsibleUsers { get; init; }
}