namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Reads provider "SyncDetails": every user the provider should synchronise, projected with the
/// project-scoped roles to emit (legacy <c>AdministrationService.GetSyncDetails</c> +
/// <c>UserService.PopulateExtendedUserInfo</c> 252-overload). Two faithful groups:
/// <list type="number">
/// <item>the provider's own users (assigned to one of the provider's projects), whose role entries are
/// filtered to the provider's projects;</item>
/// <item>every other user, whose role entries cover all of their projects.</item>
/// </list>
/// The query is a pure DB projection: it carries role <b>ids</b> (the user's <c>UserPermission.SystemRoleId</c>
/// per project), never role names — the endpoint resolves names/permissions via the Forge.Core role service.
/// </summary>
public interface ISyncDetailsQuery
{
    Task<IReadOnlyList<SyncUserView>> GetSyncDetailsAsync(Guid providerId, CancellationToken ct);
}