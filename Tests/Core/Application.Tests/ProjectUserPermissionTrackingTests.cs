using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Regression guard for issue #280 (POST /api/Project returned 409 on every call):
/// <c>ProjectService.AssignedUserToProjectWithRoleAsync</c> adds a new <see cref="UserPermission"/> to the
/// navigation collection of an already-tracked <see cref="Project"/> (via <c>context.Attach()</c>) without an explicit insert.
/// The test verifies that such an entity is correctly tracked as Added, not Modified. The model is built
/// against SQLite because production runs on a relational provider (SQL Server) and SQLite is one too; the
/// ValueGenerated metadata this hangs on is provider-independent as measured, so InMemory would be equally
/// valid — the choice buys fidelity of shape, not any additional guarantee.
/// </summary>
public sealed class ProjectUserPermissionTrackingTests
{
    [Fact(DisplayName = "A UserPermission added to a tracked Project's navigation is tracked as Added")]
    public void UserPermissionAddedToTrackedProject_IsTrackedAsAdded()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new DatabaseContext(options);

        var project = new Project();
        context.Attach(project);

        var permission = new UserPermission { Project = project };
        project.UserPermissions.Add(permission);
        context.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, context.Entry(permission).State);
    }
}
