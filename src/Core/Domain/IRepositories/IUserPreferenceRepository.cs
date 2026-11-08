using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IUserPreferenceRepository : IRepository<UserPreference, Guid>
{
    Task<UserPreference> GetUserPreferences(string idpUserId, CancellationToken ct);
    Task UpdateUserPreferences(string idpUserId, string preferencesJson, CancellationToken ct);
}