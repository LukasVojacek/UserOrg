using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

using Core.DataAccess.Contexts;
using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Project.CreateProject;
using Core.Shared.OutputCache;
using Core.UnitTests.Features.Resource.Workflow.Handlers;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;
using Forge.Core.Features.UserOrgRoles;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

/// <summary>
/// #337: the creator's GET right after <c>POST /api/v2/Projects</c> is in scope once the gateway rebuilds the
/// creator's permission snapshot. The project is created through the real endpoint and the real
/// <see cref="DatabaseContext"/>; the permissions are then read back with the gateway's query
/// (<c>UserOrgDbContext.GetUserPermissionsAsync</c>, minus the SQL Server <c>dbo.</c> schema SQLite lacks),
/// turned into the projectRoles claim the way <c>UserPermissionsCache</c> and <c>TokenService</c> do, and bound
/// into the <see cref="Caller"/> the GET endpoint scopes on.
/// </summary>
public sealed class CreateProjectReadYourWritesTests : SqliteQueryHarness
{
    private const string CreatorIdp = "creator-idp";
    private const string NewProject = "NEWPRJ";
    private const string OtherProject = "OTHERPRJ";
    private const int OwnerRoleId = 4;

    private static readonly IReadOnlyDictionary<int, string> RoleNames = new Dictionary<int, string>
    {
        [1] = "User",
        [OwnerRoleId] = "Owner",
        [3] = "Member",
    };

    protected override void AddServices(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork>(sp => new DbContextUnitOfWork(sp.GetRequiredService<DatabaseContext>()));
        services.AddScoped<IRepository<ProjectAggregate, ProjectShortName>>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IRepository<Domain.User.User, IdpId>>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IUserDirectory>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IProjectDomainTypeLookup>(sp => sp.GetRequiredService<DatabaseContext>());
    }

    private async Task SeedCreatorAsync()
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var creator = Domain.User.User.CreateUser(Email.Create("creator@example.org"), UserName.Create("creator"), new IdpId(CreatorIdp));
        creator.PromoteUserToRole(ProjectRoleId.Create(1));
        context.Add(creator);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IResult> CreateAsync(string domainTypeName)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(
            [
                new RoleInfo(OwnerRoleId, "Owner", "Owner", "Project", [], true, 20),
                new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
            ]));
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        var caller = new Caller(new IdpId(CreatorIdp), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());
        var request = new CreateProjectEndpoint.Request(
            ShortName: NewProject, Name: "New Project", Description: null, ContactEmail: null,
            StartDate: SqliteCoreSeed.Start, EndDate: SqliteCoreSeed.End, DomainTypeName: domainTypeName);

        return await CreateProjectEndpoint.HandleAsync(
            request, caller,
            sp.GetRequiredService<IRepository<ProjectAggregate, ProjectShortName>>(),
            sp.GetRequiredService<IRepository<Domain.User.User, IdpId>>(),
            sp.GetRequiredService<IUserDirectory>(),
            sp.GetRequiredService<IProjectDomainTypeLookup>(),
            roleList.Object, Options.Create(new WellKnownRoles()),
            sp.GetRequiredService<IUnitOfWork>(), cache.Object,
            new RecordingAuditLogger<CreateProjectEndpoint.AuditCategory>(),
            NullLogger<CreateProjectEndpoint.AuditCategory>.Instance,
            PermissionsChangedResponse.Create(), TestContext.Current.CancellationToken);
    }

    private async Task<IList<UserPermissionDto>> StoredPermissionsAsync(string idpUserId)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = """
            select u.IdentityProviderUserId, up.ProjectId, p.ShortName, up.SystemRoleId
            from UserPermission up
                 inner join [User] u on u.Id = up.UserId
                 left join Project p on up.ProjectId = p.Id
            where u.IdentityProviderUserId = $idp
              and u.IsDeleted = 0
              and up.IsDeleted = 0
              and (up.ProjectId is null or p.IsDeleted = 0)
            """;
        command.Parameters.AddWithValue("$idp", idpUserId);

        var permissions = new List<UserPermissionDto>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var roleId = reader.GetInt32(3);
            permissions.Add(new UserPermissionDto
            {
                IdentityProviderUserId = reader.GetString(0),
                ProjectId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ShortName = reader.IsDBNull(2) ? null : reader.GetString(2),
                SystemRoleId = roleId,
                SystemRoleName = RoleNames.TryGetValue(roleId, out var name) ? name : "Unknown",
            });
        }

        return permissions;
    }

    private static async Task<Caller> CallerFromSnapshotAsync(IList<UserPermissionDto> permissions)
    {
        var projectRecords = permissions.ToProjectRecords(new ConfigurationBuilder().Build(), out _);
        var claim = JsonSerializer.Serialize(projectRecords.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, CreatorIdp), new Claim("projectRoles", claim)], "Test")),
        };

        var caller = await Caller.BindAsync(context);
        Assert.NotNull(caller);
        return caller;
    }

    [Fact]
    public async Task Should_BeInScopeOfTheNewProject_When_TheCreatorsSnapshotIsRebuiltAfterTheCreate()
    {
        var seed = new SqliteCoreSeed(ServiceProvider);
        await SeedCreatorAsync();
        await seed.ProjectAsync(OtherProject);
        var staleSnapshot = await CallerFromSnapshotAsync(await StoredPermissionsAsync(CreatorIdp));

        var result = await CreateAsync($"domain-{OtherProject}");

        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.NotNull(staleSnapshot.RequireProjectScope(ProjectShortName.Create(NewProject)));

        var rebuilt = await CallerFromSnapshotAsync(await StoredPermissionsAsync(CreatorIdp));

        Assert.Contains(ProjectShortName.Create(NewProject), rebuilt.Scope);
        Assert.Null(rebuilt.RequireProjectScope(ProjectShortName.Create(NewProject)));
        Assert.NotNull(rebuilt.RequireProjectScope(ProjectShortName.Create(OtherProject)));
    }
}