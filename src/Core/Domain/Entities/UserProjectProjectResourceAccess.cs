using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

public sealed class UserProjectAccess : AuditableServiceIntegrationEntity<Guid>
{
    public bool HasAccess { get; set; } = true;
    public Guid UserId { get; set; }
    public ProjectId ProjectId { get; set; }
    public ProjectResource? ProjectResource { get; set; }
    public Guid? ProjectResourceId { get; set; }
    public required User AssignedByUser { get; set; }
}