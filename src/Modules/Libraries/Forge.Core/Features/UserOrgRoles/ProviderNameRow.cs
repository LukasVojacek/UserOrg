namespace Forge.Core.Features.UserOrgRoles;

/// <summary>Keyless projection for the raw-SQL provider-name lookup on <see cref="UserOrgDbContext"/>.</summary>
internal sealed class ProviderNameRow
{
    public string Name { get; init; } = string.Empty;
}