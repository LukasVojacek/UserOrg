namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.SystemRoleManagement;

public record SystemRoleModel(string Name, int Id, string? Description, IList<string> SystemPermissions);