namespace Core.Domain.User;

/// <summary>
/// Stable error codes for <see cref="UserErrors"/>. Public so the Core module can map a domain
/// <c>Error</c> to an HTTP status without string-matching the description.
/// </summary>
public static class UserErrorCodes
{
    public const string AdminRoleImmutable = "user.admin_role_immutable";
    public const string ProviderAdminRequiresProvider = "user.provider_admin_requires_provider";
    public const string CannotDemoteWithProviders = "user.cannot_demote_with_providers";
    public const string AlreadyAssignedToProvider = "user.already_assigned_to_provider";
    public const string NotAssignedToProvider = "user.not_assigned_to_provider";
    public const string RoleNotResolved = "user.role_not_resolved";
    public const string Deactivated = "user.deactivated";
}