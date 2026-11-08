namespace UserOrganizationManagement.Domain.ExtModels;

public record ProjectSystemRole(Guid UserId, Guid ProjectId, string SystemRoleName);