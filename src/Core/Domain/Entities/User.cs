using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace UserOrganizationManagement.Domain.Entities;

public class User : AuditableEntity<Guid>
{
    public string IdentityProviderUserId { get; set; } = Guid.NewGuid().ToString();
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public IList<AssignedUserToProject> AssignedToProjects { get; init; } = [];
    // TODO In future this replace table AssignedUserToProject
    //public IList<UserProjectAccess> AssignedToProjects { get; init; } = [];
    public Provider? AssignedProvider { get; set; }
}