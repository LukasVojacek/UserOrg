using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Domain.Entities;

public class ActionLink : AuditableEntity<Guid>
{
    public EActionType Action { get; set; }
    public string? Link { get; set; }
    public Request? Request { get; set; }

    public DateTime ValidTo { get; set; }
    public Guid RequestId { get; set; }
}