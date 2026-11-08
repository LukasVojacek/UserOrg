using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Domain.Entities;

public class Request : AuditableEntity<Guid>
{
    public User? User { get; set; }
    public Project? Project { get; set; }
    public ProjectResource? ProjectResource { get; set; }
    public string? ProjectResourceName { get; set; }
    public ERequestState State { get; set; }
    public ERequestType Type { get; set; }
    public Provider? Provider { get; set; }
    public string? ContactEmail { get; set; }
    public string? Slug { get; set; }
    public IEnumerable<ActionLink>? ActionLinks { get; set; }
}