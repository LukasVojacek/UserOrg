namespace Core.Domain.User;

public sealed class WellKnownRoles
{
    public string DefaultUserRoleName { get; init; } = "User";
    public string DefaultAdminRoleName { get; init; } = "Admin";
    public string DefaultProviderAdminRoleName { get; init; } = "ProviderAdmin";
    public string DefaultProjectOwnerRoleName { get; init; } = "Owner";
    public string DefaultProjectManagerRoleName { get; init; } = "Manager";
    public string DefaultProjectMemberRoleName { get; init; } = "Member";
}