using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Regression guard for the GET /api/User/Preferences 500: the unused SystemModule navigation must not
/// be mapped, or EF emits SystemModuleId in every SELECT on UserPreference and fails with SqlException 207
/// wherever that column is absent from the live schema.
/// </summary>
public sealed class UserPreferenceMappingTests
{
    private static DatabaseContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(nameof(UserPreferenceMappingTests))
            .Options;
        return new DatabaseContext(options);
    }

    [Fact(DisplayName = "UserPreference is not mapped with a SystemModuleId column")]
    public void UserPreference_DoesNotMapSystemModuleIdColumn()
    {
        using var context = BuildContext();

        var entity = context.Model.FindEntityType(typeof(UserPreference));

        Assert.NotNull(entity);
        Assert.Null(entity!.FindProperty("SystemModuleId"));
        Assert.Null(entity.FindNavigation(nameof(UserPreference.SystemModule)));
    }
}
