using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class Location : AuditableServiceIntegrationEntity<Guid>
{
    public Guid ProviderId { get; set; }
    public int LocationTypeId { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Endpoint { get; set; }
    public bool IsPrivate { get; set; }
    public Provider? Provider { get; set; }
    public LocationType? LocationType { get; set; }
    public IList<AssignedLocation> AssignedLocations { get; private set; } = [];
    public bool IsShared { get; set; } = true;
    public Uri? DefaultServiceIntegrationUrl { get; set; }
}