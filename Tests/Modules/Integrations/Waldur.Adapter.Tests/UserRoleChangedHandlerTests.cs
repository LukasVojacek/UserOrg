using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Sync.Handlers;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Tests for the B09 <see cref="UserRoleChangedHandler"/>: updates the project-scoped
/// <see cref="UserPermission"/> + <see cref="UserProjectAccess"/> for an existing membership, using
/// the same role map + unknown fallback, WITHOUT rewriting the create-only user profile (D16).
/// </summary>
public sealed class UserRoleChangedHandlerTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private const string UserName = "alice";
    private const string OriginalEmail = "alice@efp.test";

    private static readonly string ProjectExternalId = Guid.NewGuid().ToString();

    private static readonly RoleInfo OwnerRole = new(1, RoleInfoConstants.ProjectOwnerRoleName, "", "Project", [], true, 3);
    private static readonly RoleInfo ManagerRole = new(2, RoleInfoConstants.ProjectManagerRoleName, "", "Project", [], true, 2);
    private static readonly RoleInfo MemberRole = new(3, RoleInfoConstants.ProjectMemberRoleName, "", "Project", [], true, 1);
    private static readonly RoleInfo PlatformUserRole = new(4, RoleInfoConstants.PlatformUserRoleName, "", "System", [], true, 0);

    private static IList<RoleInfo> AllRoles => [OwnerRole, ManagerRole, MemberRole, PlatformUserRole];

    private static ProjectUserPayload Payload(string role, bool hasAccess = true) =>
        new(ProjectExternalId, UserName, role, hasAccess) { Email = "IGNORED@efp.test" };

    private static UserRoleChanged Changed(ProjectUserPayload payload) =>
        new(payload) { Instance = Instance, ServiceName = ServiceName };

    private static UserRoleChangedHandler Handler(IUserOrgDbContext context) =>
        new(context, new FakeRoleListService(AllRoles), NullLogger<UserRoleChangedHandler>.Instance);

    private sealed record Seeded(ProjectId ProjectId, Guid UserId);

    /// <summary>Seeds service account, project, user (Member role, access granted).</summary>
    private static Seeded Seed(DbContext context)
    {
        context.Add(new User { Id = Guid.CreateVersion7(), IdentityProviderUserId = ServiceName, Email = "svc@waldur", UserName = ServiceName, CreatedAt = DateTime.UtcNow });

        var userId = Guid.CreateVersion7();
        var user = new User { Id = userId, IdentityProviderUserId = UserName, Email = OriginalEmail, UserName = UserName, CreatedAt = DateTime.UtcNow };
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

        context.Add(new UserPermission { Id = Guid.CreateVersion7(), User = user, Project = project, SystemRoleId = MemberRole.Id, CreatedAt = DateTime.UtcNow });
        context.Add(new UserProjectAccess { Id = Guid.CreateVersion7(), ProjectId = projectId, UserId = userId, HasAccess = true, AssignedByUser = user, ServiceName = ServiceName, Mode = EOperationMode.ReadOnly, CreatedAt = DateTime.UtcNow });

        context.SaveChanges();
        return new Seeded(projectId, userId);
    }

    [Fact(DisplayName = "Updates the project permission role for an existing membership")]
    public async Task Should_UpdatePermissionRole_When_Changed()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);

        await Handler(context).HandleAsync(Changed(Payload(RoleInfoConstants.ProjectOwnerRoleName)), CancellationToken.None);

        var permission = await context.UserPermissionsQuery.SingleAsync(p => p.User!.Id == seeded.UserId && p.Project!.Id == seeded.ProjectId);
        Assert.Equal(OwnerRole.Id, permission.SystemRoleId);
    }

    [Fact(DisplayName = "Updates the project access flag for an existing membership")]
    public async Task Should_UpdateAccess_When_Changed()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);

        await Handler(context).HandleAsync(Changed(Payload(RoleInfoConstants.ProjectMemberRoleName, hasAccess: false)), CancellationToken.None);

        var access = await context.UserProjectAccessesQuery.SingleAsync(a => a.ProjectId == seeded.ProjectId && a.UserId == seeded.UserId);
        Assert.False(access.HasAccess);
    }

    [Fact(DisplayName = "Does NOT rewrite the create-only user profile (email)")]
    public async Task Should_NotRewriteProfile_When_Changed()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);

        await Handler(context).HandleAsync(Changed(Payload(RoleInfoConstants.ProjectOwnerRoleName)), CancellationToken.None);

        var user = await context.UsersQuery.SingleAsync(u => u.Id == seeded.UserId);
        Assert.Equal(OriginalEmail, user.Email); // create-only profile untouched (D16)
    }

    [Fact(DisplayName = "Unknown mapped role falls back to ProjectMember")]
    public async Task Should_FallBackToMember_When_RoleUnknown()
    {
        using var context = new FakeUserOrgDbContext();
        var seeded = Seed(context);
        // start at Owner so a fallback to Member is observable
        await Handler(context).HandleAsync(Changed(Payload(RoleInfoConstants.ProjectOwnerRoleName)), CancellationToken.None);

        await Handler(context).HandleAsync(Changed(Payload("NotARole")), CancellationToken.None);

        var permission = await context.UserPermissionsQuery.SingleAsync(p => p.User!.Id == seeded.UserId && p.Project!.Id == seeded.ProjectId);
        Assert.Equal(MemberRole.Id, permission.SystemRoleId);
    }

    [Fact(DisplayName = "Returns failure (not acked) when the project is not found for (ExternalId, ServiceName)")]
    public async Task Should_ReturnFailure_When_ProjectMissing()
    {
        using var context = new FakeUserOrgDbContext();
        Seed(context);

        var otherProjectEvent = new UserRoleChanged(new ProjectUserPayload(Guid.NewGuid().ToString(), UserName, RoleInfoConstants.ProjectOwnerRoleName, true))
        {
            Instance = Instance,
            ServiceName = ServiceName,
        };

        var result = await Handler(context).HandleAsync(otherProjectEvent, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
