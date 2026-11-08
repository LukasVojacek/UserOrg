using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class UserPreferenceRepository(DatabaseContext context) : GenericRepository<UserPreference, Guid>(context), IUserPreferenceRepository
{
    public async Task<UserPreference> GetUserPreferences(string idpUserId, CancellationToken ct)
    {
        User user = await Context.Users
                .FirstOrDefaultAsync(x => x.IdentityProviderUserId == idpUserId, ct)
                ?? throw new InvalidOperationException($"User with idpUserId '{idpUserId}' not found.");

        UserPreference preferences = await Context.UserPreferences
            .FirstOrDefaultAsync(x => x.User.Id == user.Id, ct)
            ;

        if (preferences is null)
        {
            preferences = new UserPreference
            {
                User = user,
                Data = "{}",
                CreatedAt = DateTime.UtcNow,
            };
            Context.Add(preferences);
        }

        await Context.SaveChangesAsync(ct);
        return preferences;
    }

    public async Task UpdateUserPreferences(string idpUserId, string preferencesJson, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(preferencesJson))
        {
            preferencesJson = "{}";
        }

        User user = await Context.Users
                .FirstOrDefaultAsync(x => x.IdentityProviderUserId == idpUserId, ct)
                ?? throw new InvalidOperationException($"User with idpUserId '{idpUserId}' not found.");

        UserPreference preferences = await Context.UserPreferences
            .FirstOrDefaultAsync(x => x.User.Id == user.Id, ct)
            ;

        if (preferences is null)
        {
            preferences = new UserPreference
            {
                User = user,
                Data = preferencesJson,
                CreatedAt = DateTime.UtcNow,
            };
            Context.Add(preferences);
        }

        preferences.Data = preferencesJson;
        Context.Update(preferences);

        await Context.SaveChangesAsync(ct);
    }
}