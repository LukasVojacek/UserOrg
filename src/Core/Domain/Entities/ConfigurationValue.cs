using UserOrg.Shared.Persistance.Abstractions.Interfaces;

namespace UserOrganizationManagement.Domain.Entities;

public class ConfigurationValue : IAuditableEntity
{
    public string? Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Location? Location { get; set; }
    public Configuration? Configuration { get; set; }
    public ConfigurationType? ConfigurationType { get; set; }
}