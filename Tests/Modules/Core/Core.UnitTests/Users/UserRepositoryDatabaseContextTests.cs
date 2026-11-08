using Core.DataAccess;
using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Exceptions;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.Users;

/// <summary>
/// Characterization tests (issue #253 audit risky spot 2) for
/// <c>DatabaseContext.UserRepository.cs</c>. Pins the behaviour of both <c>AddAsync</c> overloads
/// (<c>IRepository&lt;User, UserName&gt;</c> and <c>IRepository&lt;User, IdpId&gt;</c>) — including the
/// ADR-0068 C2 first-user bootstrap: an empty <c>Users</c> table resolves <c>DefaultAdminRoleName</c>,
/// a non-empty table resolves <c>DefaultUserRoleName</c> — plus <c>FindAsync</c>/<c>HydrateUser</c> and
/// <c>ValidateSystemRole</c>. Mocks only at the role-service port boundary.
/// </summary>
public sealed class UserRepositoryDatabaseContextTests
{
    private static readonly Dictionary<int, string> RoleMap = new()
    {
        { 1, "User" },
        { 2, "Admin" },
        { 7, "ProviderAdmin" },
    };

    private static Mock<IRoleService> SuccessfulRoleService() => Mock(Result<IReadOnlyDictionary<int, string>, Error>.Success(RoleMap));

    private static Mock<IRoleService> FailingRoleService()
    {
        var error = new ErrorBuilder().WithDescription("role service down").WithSeverity(ErrorSeverity.Error).Build();
        return Mock(Result<IReadOnlyDictionary<int, string>, Error>.Failure(error));
    }

    private static Mock<IRoleService> Mock(Result<IReadOnlyDictionary<int, string>, Error> result)
    {
        var mock = new Mock<IRoleService>();
        mock.Setup(s => s.ListRoleNames(It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return mock;
    }

    private static ServiceProvider BuildServiceProvider(IRoleService roleService)
    {
        var dbName = Guid.NewGuid().ToString();
        return BuildServiceProvider(roleService, dbName);
    }

    private static ServiceProvider BuildServiceProvider(IRoleService roleService, string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddSingleton(roleService);
        services.Configure<Core.Domain.User.WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));

        return services.BuildServiceProvider();
    }

    private static UserAggregate NewUser(string idp) =>
        UserAggregate.CreateUser(Email.Create($"{idp}@example.com"), UserName.Create(idp), IdpId.Create(idp));

    [Fact]
    public async Task Should_PromoteToDefaultAdminRole_When_AddAsyncByUserNameOverloadCalledOnEmptyTable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, UserName>)context;
        var user = NewUser("username-add");

        var added = await repo.AddAsync(user, ct);
        await context.SaveChangesAsync(ct);

        Assert.NotNull(added);
        Assert.Equal(1, added!.SystemPermissionsCount);

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var verifyRepo = (IRepository<UserAggregate, UserName>)verifyContext;
        var found = await verifyRepo.FindAsync(UserName.Create("username-add"), ct);

        Assert.NotNull(found);
        Assert.Equal("Admin", found!.SystemRoleName);
    }

    [Fact]
    public async Task Should_PromoteToDefaultUserRole_When_AddAsyncByUserNameOverloadCalledOnNonEmptyTable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);

        await using (var seedScope = sp.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var seedRepo = (IRepository<UserAggregate, UserName>)seedContext;
            await seedRepo.AddAsync(NewUser("username-first"), ct);
            await seedContext.SaveChangesAsync(ct);
        }

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, UserName>)context;
        var user = NewUser("username-second");

        var added = await repo.AddAsync(user, ct);
        await context.SaveChangesAsync(ct);

        Assert.NotNull(added);
        Assert.Equal(1, added!.SystemPermissionsCount);

        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var verifyRepo = (IRepository<UserAggregate, UserName>)verifyContext;
        var found = await verifyRepo.FindAsync(UserName.Create("username-second"), ct);

        Assert.NotNull(found);
        Assert.Equal("User", found!.SystemRoleName);
    }

    [Fact]
    public async Task Should_AssignFreshId_When_AddAsyncByUserNameOverloadCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, UserName>)context;
        var user = NewUser("fresh-id");

        var added = await repo.AddAsync(user, ct);

        var id = (UserId)context.Entry(added!).Property("Id").CurrentValue!;
        Assert.NotEqual(default, id);
    }

    [Fact]
    public async Task Should_ThrowRoleServiceException_When_AddAsyncByUserNameOverloadAndRoleServiceFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(FailingRoleService().Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, UserName>)context;
        var user = NewUser("role-fail");

        await Assert.ThrowsAsync<RoleServiceException>(async () => await repo.AddAsync(user, ct));
    }

    [Fact]
    public async Task Should_ThrowRoleServiceException_When_AddAsyncOnEmptyTableAndConfiguredAdminRoleNameMissingFromRoleService()
    {
        var ct = TestContext.Current.CancellationToken;
        var roleServiceWithoutAdmin = Mock(Result<IReadOnlyDictionary<int, string>, Error>.Success(
            new Dictionary<int, string> { { 1, "User" } }));
        await using var sp = BuildServiceProvider(roleServiceWithoutAdmin.Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, UserName>)context;
        var user = NewUser("missing-admin-role");

        // Same exception type (and therefore the same GlobalExceptionHandler mapping — 502, not the
        // unmapped-default 500) as "the role service call itself failed": a role service that does not
        // serve a role we are configured to require is the same class of upstream-contract failure.
        await Assert.ThrowsAsync<RoleServiceException>(async () => await repo.AddAsync(user, ct));
    }

    [Fact]
    public async Task Should_PromoteToDefaultAdminRole_When_AddAsyncByIdpIdOverloadCalledOnEmptyTable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;
        var user = NewUser("idp-add");

        var added = await repo.AddAsync(user, ct);
        await context.SaveChangesAsync(ct);

        Assert.NotNull(added);
        // Verify via FindAsync hydration that exactly one system permission was persisted.
        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var verifyRepo = (IRepository<UserAggregate, IdpId>)verifyContext;
        var found = await verifyRepo.FindAsync(IdpId.Create("idp-add"), ct);

        Assert.NotNull(found);
        Assert.Equal("Admin", found!.SystemRoleName);
    }

    [Fact]
    public async Task Should_PromoteToDefaultUserRole_When_AddAsyncByIdpIdOverloadCalledOnNonEmptyTable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);

        await using (var seedScope = sp.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var seedRepo = (IRepository<UserAggregate, IdpId>)seedContext;
            await seedRepo.AddAsync(NewUser("idp-first"), ct);
            await seedContext.SaveChangesAsync(ct);
        }

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;
        var user = NewUser("idp-second");

        var added = await repo.AddAsync(user, ct);
        await context.SaveChangesAsync(ct);

        Assert.NotNull(added);
        await using var verifyScope = sp.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var verifyRepo = (IRepository<UserAggregate, IdpId>)verifyContext;
        var found = await verifyRepo.FindAsync(IdpId.Create("idp-second"), ct);

        Assert.NotNull(found);
        Assert.Equal("User", found!.SystemRoleName);
    }

    [Fact]
    public async Task Should_AssignFreshId_When_AddAsyncByIdpIdOverloadCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;
        var user = NewUser("idp-fresh-id");

        var added = await repo.AddAsync(user, ct);

        var id = (UserId)context.Entry(added!).Property("Id").CurrentValue!;
        Assert.NotEqual(default, id);
    }

    [Fact]
    public async Task Should_ThrowRoleServiceException_When_AddAsyncByIdpIdOverloadAndRoleServiceFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(FailingRoleService().Object);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;
        var user = NewUser("idp-role-fail");

        await Assert.ThrowsAsync<RoleServiceException>(async () => await repo.AddAsync(user, ct));
    }

    [Fact]
    public async Task Should_HydrateRoleMap_When_FindAsyncByIdpIdLocatesExistingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);

        await using (var seedScope = sp.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var seedRepo = (IRepository<UserAggregate, IdpId>)seedContext;
            // First user persisted before the second so the table is non-empty (this test stays about
            // hydration, not bootstrap).
            await seedRepo.AddAsync(NewUser("hydrate-filler"), ct);
            await seedContext.SaveChangesAsync(ct);
            await seedRepo.AddAsync(NewUser("hydrate-me"), ct);
            await seedContext.SaveChangesAsync(ct);
        }

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;

        var found = await repo.FindAsync(IdpId.Create("hydrate-me"), ct);

        Assert.NotNull(found);
        // SystemRoleName resolving without throwing proves InitializeRoleList (role-map hydration) ran.
        Assert.Equal("User", found!.SystemRoleName);
    }

    [Fact]
    public async Task Should_ThrowRoleServiceException_When_FindAsyncAndRoleServiceFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = Guid.NewGuid().ToString();

        await using (var seedSp = BuildServiceProvider(SuccessfulRoleService().Object, dbName))
        await using (var seedScope = seedSp.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var seedRepo = (IRepository<UserAggregate, IdpId>)seedContext;
            await seedRepo.AddAsync(NewUser("find-role-fail"), ct);
            await seedContext.SaveChangesAsync(ct);
        }

        await using var failingSp = BuildServiceProvider(FailingRoleService().Object, dbName);
        await using var scope = failingSp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;

        await Assert.ThrowsAsync<RoleServiceException>(async () => await repo.FindAsync(IdpId.Create("find-role-fail"), ct));
    }

    [Fact]
    public async Task Should_ThrowInvalidUserRoleStateException_When_FindAsyncLocatesUserWithZeroSystemPermissions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);

        await using (var seedScope = sp.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            // Seed directly via CreateUser — bypasses AddAsync/PromoteUserToRole, so no permission exists.
            var user = NewUser("zero-perms");
            seedContext.Add(user);
            seedContext.Entry(user).Property("Id").CurrentValue = UserId.New();
            await seedContext.SaveChangesAsync(ct);
        }

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;

        await Assert.ThrowsAsync<InvalidUserRoleStateException>(async () => await repo.FindAsync(IdpId.Create("zero-perms"), ct));
    }

    [Fact]
    public async Task Should_ThrowInvalidUserRoleStateException_When_FindAsyncLocatesUserWithMultipleSystemPermissions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildServiceProvider(SuccessfulRoleService().Object);

        UserId userId;
        await using (var seedScope = sp.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var user = NewUser("multi-perms");
            seedContext.Add(user);
            userId = UserId.New();
            seedContext.Entry(user).Property("Id").CurrentValue = userId;
            await seedContext.SaveChangesAsync(ct);
        }

        await using (var permScope = sp.CreateAsyncScope())
        {
            var permContext = permScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            foreach (var roleId in new[] { 1, 2 })
            {
                var permission = Core.Domain.User.UserPermission.CreateSystemPermission(new ProjectRoleId(roleId));
                permContext.Add(permission);
                permContext.Entry(permission).Property("Id").CurrentValue = Guid.CreateVersion7();
                permContext.Entry(permission).Property("UserId").CurrentValue = userId;
            }
            await permContext.SaveChangesAsync(ct);
        }

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repo = (IRepository<UserAggregate, IdpId>)context;

        await Assert.ThrowsAsync<InvalidUserRoleStateException>(async () => await repo.FindAsync(IdpId.Create("multi-perms"), ct));
    }
}
