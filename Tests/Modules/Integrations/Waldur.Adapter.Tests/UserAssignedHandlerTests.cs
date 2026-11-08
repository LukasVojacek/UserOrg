using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Sync;
using Userorg.Adapter.Waldur.Sync.Handlers;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Tests for the B09 <see cref="UserAssignedHandler"/>: writes the FIVE membership row kinds
/// (ADR-0057/A4) — User (create-only profile), AssignedUserToProject, project-scoped UserPermission,
/// system PlatformUser UserPermission, UserProjectAccess — with a single terminal save.
/// </summary>
public sealed class UserAssignedHandlerTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private const string UserName = "alice";
    private const string Email = "alice@efp.test";

    private static readonly string ProjectExternalId = Guid.NewGuid().ToString();

    private static readonly RoleInfo OwnerRole = new(1, RoleInfoConstants.ProjectOwnerRoleName, "", "Project", [], true, 3);
    private static readonly RoleInfo ManagerRole = new(2, RoleInfoConstants.ProjectManagerRoleName, "", "Project", [], true, 2);
    private static readonly RoleInfo MemberRole = new(3, RoleInfoConstants.ProjectMemberRoleName, "", "Project", [], true, 1);
    private static readonly RoleInfo PlatformUserRole = new(4, RoleInfoConstants.PlatformUserRoleName, "", "System", [], true, 0);

    private static IList<RoleInfo> AllRoles => [OwnerRole, ManagerRole, MemberRole, PlatformUserRole];

    private static ProjectUserPayload Payload(
        string role = RoleInfoConstants.ProjectManagerRoleName,
        bool hasAccess = true,
        string? email = Email,
        string userName = UserName) =>
        new(ProjectExternalId, userName, role, hasAccess) { Email = email };

    private static UserAssigned Assigned(ProjectUserPayload payload) =>
        new(payload) { Instance = Instance, ServiceName = ServiceName };

    private static UserAssignedHandler Handler(IUserOrgDbContext context, IList<RoleInfo>? roles = null) =>
        new(context, new FakeRoleListService(roles ?? AllRoles), NullLogger<UserAssignedHandler>.Instance);

    private static ProjectId SeedProjectAndAccount(DbContext context, bool seedServiceAccount = true)
    {
        if (seedServiceAccount)
        {
            context.Add(new User
            {
                Id = Guid.CreateVersion7(),
                IdentityProviderUserId = ServiceName,
                Email = "svc@waldur",
                UserName = ServiceName,
                CreatedAt = DateTime.UtcNow,
            });
        }

        var projectId = ProjectId.Create(Guid.CreateVersion7());
        context.Add(new Project
        {
            Id = projectId,
            Name = "Acme",
            ShortName = ProjectShortName.Create("acme"),
            ServiceName = ServiceName,
            ExternalId = ProjectExternalId,
            ProjectDomainType = new ProjectDomainType { Name = "EFP-Service" },
            CreatedAt = DateTime.UtcNow,
        });
        context.SaveChanges();
        return projectId;
    }

    private static FakeUserOrgDbContext SeededContext(bool seedServiceAccount = true)
    {
        var context = new FakeUserOrgDbContext();
        SeedProjectAndAccount(context, seedServiceAccount);
        return context;
    }

    [Fact(DisplayName = "Writes all five membership row kinds on a first assignment")]
    public async Task Should_WriteAllFiveRowKinds_When_Assigned()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Assigned(Payload()), CancellationToken.None);

        var user = await context.UsersQuery.SingleAsync(u => u.UserName == UserName);
        Assert.Equal(Email, user.Email);

        var project = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        Assert.NotNull(await context.AssignedUserToProjectsQuery.SingleOrDefaultAsync(a => a.ProjectId == project.Id && a.User!.Id == user.Id));

        var projectPermission = await context.UserPermissionsQuery.SingleOrDefaultAsync(p => p.User!.Id == user.Id && p.Project!.Id == project.Id);
        Assert.NotNull(projectPermission);
        Assert.Equal(ManagerRole.Id, projectPermission!.SystemRoleId);

        var systemPermission = await context.UserPermissionsQuery.SingleOrDefaultAsync(p => p.User!.Id == user.Id && p.Project == null);
        Assert.NotNull(systemPermission);
        Assert.Equal(PlatformUserRole.Id, systemPermission!.SystemRoleId);

        var access = await context.UserProjectAccessesQuery.SingleOrDefaultAsync(a => a.ProjectId == project.Id && a.UserId == user.Id);
        Assert.NotNull(access);
        Assert.True(access!.HasAccess);
    }

    [Fact(DisplayName = "Unknown mapped role falls back to ProjectMember")]
    public async Task Should_FallBackToMember_When_RoleUnknown()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Assigned(Payload(role: "NotARole")), CancellationToken.None);

        var user = await context.UsersQuery.SingleAsync(u => u.UserName == UserName);
        var project = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        var projectPermission = await context.UserPermissionsQuery.SingleAsync(p => p.User!.Id == user.Id && p.Project!.Id == project.Id);
        Assert.Equal(MemberRole.Id, projectPermission.SystemRoleId);
    }

    [Fact(DisplayName = "Email is written on create only, not overwritten on a later assign")]
    public async Task Should_NotOverwriteEmail_When_ReAssigned()
    {
        using var context = SeededContext();
        await Handler(context).HandleAsync(Assigned(Payload(email: Email)), CancellationToken.None);

        await Handler(context).HandleAsync(Assigned(Payload(email: "changed@efp.test", role: RoleInfoConstants.ProjectOwnerRoleName)), CancellationToken.None);

        var user = await context.UsersQuery.SingleAsync(u => u.UserName == UserName);
        Assert.Equal(Email, user.Email); // create-only profile (D16)
        var project = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        var projectPermission = await context.UserPermissionsQuery.SingleAsync(p => p.User!.Id == user.Id && p.Project!.Id == project.Id);
        Assert.Equal(OwnerRole.Id, projectPermission.SystemRoleId); // role still updated
    }

    [Fact(DisplayName = "Revives the soft-deleted project permission on a re-add after removal")]
    public async Task Should_ReviveProjectPermission_When_ReAddedAfterRemoval()
    {
        using var context = SeededContext();
        await Handler(context).HandleAsync(Assigned(Payload(role: RoleInfoConstants.ProjectManagerRoleName)), CancellationToken.None);

        // Remove the member: the project-scoped permission is soft-deleted (B16).
        var removed = new UserRemoved(ProjectExternalId, UserName) { Instance = Instance, ServiceName = ServiceName };
        await new UserRemovedHandler(context).HandleAsync(removed, CancellationToken.None);

        // Re-add: the previously soft-deleted permission must be revived, not left dangling.
        await Handler(context).HandleAsync(Assigned(Payload(role: RoleInfoConstants.ProjectOwnerRoleName)), CancellationToken.None);

        var user = await context.UsersQuery.SingleAsync(u => u.UserName == UserName);
        var project = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        var projectPermission = await context.UserPermissionsQuery
            .Include(p => p.Project)
            .SingleAsync(p => p.User!.Id == user.Id && p.Project!.Id == project.Id);
        Assert.False(projectPermission.IsDeleted); // role restored
        Assert.Equal(OwnerRole.Id, projectPermission.SystemRoleId); // and updated
    }

    [Fact(DisplayName = "Returns failure (not acked) when the service account is not bootstrapped (B06/F1)")]
    public async Task Should_ReturnFailure_When_ServiceAccountMissing()
    {
        using var context = SeededContext(seedServiceAccount: false);

        var result = await Handler(context).HandleAsync(Assigned(Payload()), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact(DisplayName = "Returns failure (not acked) when the project is not found for (ExternalId, ServiceName)")]
    public async Task Should_ReturnFailure_When_ProjectMissing()
    {
        using var context = new FakeUserOrgDbContext();
        context.Add(new User { Id = Guid.CreateVersion7(), IdentityProviderUserId = ServiceName, Email = "svc@waldur", UserName = ServiceName, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(context).HandleAsync(Assigned(Payload()), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact(DisplayName = "Membership handlers are registered via the B06 seam")]
    public void Should_RegisterMembershipHandlers_For_AllThreeEvents()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddScoped<IRoleListService>(_ => new FakeRoleListService(AllRoles));
        services.AddLogging();
        services.AddWaldurEventConsumers();
        services.AddWaldurMembershipHandlers();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<UserAssigned>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<UserRoleChanged>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<UserRemoved>>());
    }
}
