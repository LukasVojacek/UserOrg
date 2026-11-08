using Microsoft.EntityFrameworkCore;

using Userorg.Adapter.Waldur.Sync.Handlers;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts.Events;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Tests for <see cref="UserRemovedHandler"/>: TRIPLE soft-delete of the
/// <see cref="AssignedUserToProject"/>, <see cref="UserProjectAccess"/>, and project-scoped
/// <see cref="UserPermission"/> rows for the <c>(project, userName)</c> pair (ADR-0057/A4, ADR-0054/A2),
/// with preservation of the system-level PlatformUser permission. Also tests idempotent no-op replay
/// when the member is already absent or already deleted.
/// </summary>
public sealed class UserRemovedHandlerTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private const string UserName = "alice";

    private static readonly string ProjectExternalId = Guid.NewGuid().ToString();

    private static UserRemoved Removed(string userName = UserName) =>
        new(ProjectExternalId, userName) { Instance = Instance, ServiceName = ServiceName };

    private static UserRemovedHandler Handler(IUserOrgDbContext context) => new(context);

    private sealed record Seeded(ProjectId ProjectId, Guid UserId);

    private static Seeded Seed(DbContext context, bool withAssignment = true, bool withAccess = true)
    {
        var userId = Guid.CreateVersion7();
        var user = new User { Id = userId, IdentityProviderUserId = UserName, Email = "alice@efp.test", UserName = UserName, CreatedAt = DateTime.UtcNow };
        context.Add(user);

        var projectId = ProjectId.Create(Guid.CreateVersion7());
        var project = new Project
        {
            Id = projectId,
            Name = "Acme",
            ShortName = ProjectShortName.Create("acme"),
            ServiceName = ServiceName,
            ExternalId = ProjectExternalId,
            ProjectDomainType = new ProjectDomainType { Name = "EFP-Service" },
            CreatedAt = DateTime.UtcNow,
        };
        context.Add(project);

        if (withAssignment)
        {
            context.Add(new AssignedUserToProject { ProjectId = projectId, Project = project, User = user, AssignedByUser = user, CreatedAt = DateTime.UtcNow, IsDeleted = false });
        }

        if (withAccess)
        {
            context.Add(new UserProjectAccess { Id = Guid.CreateVersion7(), ProjectId = projectId, UserId = userId, HasAccess = true, AssignedByUser = user, ServiceName = ServiceName, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow, IsDeleted = false });
        }

        // Project-scoped permission (the role that keeps the member effective) + system PlatformUser
        // permission (platform identity that must SURVIVE a project removal).
        context.Add(new UserPermission { Id = Guid.CreateVersion7(), User = user, Project = project, SystemRoleId = 3, CreatedAt = DateTime.UtcNow, IsDeleted = false });
        context.Add(new UserPermission { Id = Guid.CreateVersion7(), User = user, SystemRoleId = 4, CreatedAt = DateTime.UtcNow, IsDeleted = false });

        context.SaveChanges();
        return new Seeded(projectId, userId);
    }

    [Fact(DisplayName = "Soft-deletes BOTH the assignment and the access for (project, userName)")]
    public async Task Should_DualSoftDelete_When_MemberPresent()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);

        await Handler(context).HandleAsync(Removed(), CancellationToken.None);

        var assignment = await context.AssignedUserToProjectsQuery.Include(a => a.User).SingleAsync(a => a.ProjectId == seeded.ProjectId && a.User!.Id == seeded.UserId);
        Assert.True(assignment.IsDeleted);
        var access = await context.UserProjectAccessesQuery.SingleAsync(a => a.ProjectId == seeded.ProjectId && a.UserId == seeded.UserId);
        Assert.True(access.IsDeleted);
    }

    [Fact(DisplayName = "Soft-deletes the project-scoped UserPermission but PRESERVES the system PlatformUser permission")]
    public async Task Should_SoftDeleteProjectPermissionButKeepSystemPermission_When_MemberPresent()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);

        await Handler(context).HandleAsync(Removed(), CancellationToken.None);

        // The project-scoped role must be revoked — this is what GetUserPermissionsAsync reads.
        var projectPermission = await context.UserPermissionsQuery
            .Include(p => p.Project)
            .SingleAsync(p => p.User!.Id == seeded.UserId && p.Project!.Id == seeded.ProjectId);
        Assert.True(projectPermission.IsDeleted);

        // The system-level PlatformUser identity must SURVIVE removal from one project.
        var systemPermission = await context.UserPermissionsQuery
            .SingleAsync(p => p.User!.Id == seeded.UserId && p.Project == null);
        Assert.False(systemPermission.IsDeleted);
    }

    [Fact(DisplayName = "No-op (does not throw) when the user was never materialised")]
    public async Task Should_NoOp_When_UserAbsent()
    {
        using var context = new FakeUserOrgDbContext();
        Seed(context);

        // A different user name that does not exist — replay against an absent member.
        await Handler(context).HandleAsync(Removed(userName: "ghost"), CancellationToken.None);

        // The seeded member is untouched.
        var assignment = await context.AssignedUserToProjectsQuery.SingleAsync();
        Assert.False(assignment.IsDeleted);
    }

    [Fact(DisplayName = "No-op (idempotent) when the assignment and access are already soft-deleted")]
    public async Task Should_NoOp_When_AlreadyDeleted()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);
        await Handler(context).HandleAsync(Removed(), CancellationToken.None);

        // Second replay must not throw and must leave rows deleted (idempotent).
        await Handler(context).HandleAsync(Removed(), CancellationToken.None);

        var assignment = await context.AssignedUserToProjectsQuery.Include(a => a.User).SingleAsync(a => a.ProjectId == seeded.ProjectId && a.User!.Id == seeded.UserId);
        Assert.True(assignment.IsDeleted);
    }

    [Fact(DisplayName = "No-op when the project is not found for (ExternalId, ServiceName)")]
    public async Task Should_NoOp_When_ProjectAbsent()
    {
        using var context = new FakeUserOrgDbContext();

        // No project seeded at all.
        await Handler(context).HandleAsync(Removed(), CancellationToken.None);
        // Reaching here without throwing is the assertion.
    }
}
