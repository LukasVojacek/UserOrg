namespace Forge.Core.Features.UserOrgRoles;

public sealed record UserPermissionDto
{
    public required string IdentityProviderUserId { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ShortName { get; init; }

    public required int SystemRoleId { get; init; }

    public required string SystemRoleName { get; init; }
}