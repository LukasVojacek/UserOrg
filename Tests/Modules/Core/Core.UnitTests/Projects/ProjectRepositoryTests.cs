using Core.DataAccess.Contexts;
using Core.Domain;
using Core.Domain.Shared;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.Projects;

/// <summary>
/// The project repository and the real <see cref="IUnitOfWork"/> save path over SQLite: removing a resource
/// soft-deletes its link, re-assigning a removed resource or user reactivates the same link row (the composite
/// key allows only one), and a loaded project still hides removed links. Each act runs in a fresh scope the way
/// the unassign and assign endpoints do; the durable domain events are cleared before the save because the
/// outbox leg is bound to SQL Server.
/// </summary>
public sealed class ProjectRepositoryTests : SqliteQueryHarness
{
    private const string ProjectShortName = "REPOPRJ";
    private const string ProviderName = "repo-provider";
    private const string ResourceName = "repo-resource";
    private const string ActiveResourceName = "repo-active";
    private const string RemovedMemberIdp = "repo-removed-member";
    private const string ActiveMemberIdp = "repo-active-member";
    private const int MemberRoleId = 3;

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DatabaseContext>());

    [Fact]
    public async Task Should_SoftDeleteLink_When_ResourceIsRemoved()
    {
        var (_, projectId, resourceId) = await SeedAsync(linkDeleted: false);

        var saveError = await ActAsync(resourceId, (project, resource) => project.RemoveResource(resource));

        Assert.Null(saveError);
        Assert.Equal([true], await ReadLinkStatesAsync(projectId, resourceId));
    }

    [Fact]
    public async Task Should_ReactivateLink_When_RemovedResourceIsReassigned()
    {
        var (_, projectId, resourceId) = await SeedAsync(linkDeleted: true);

        var saveError = await ActAsync(resourceId, (project, resource) => project.AssignResource(resource));

        Assert.Null(saveError);
        Assert.Equal([false], await ReadLinkStatesAsync(projectId, resourceId));
    }

    [Fact]
    public async Task Should_HideDeletedLink_When_ProjectIsLoaded()
    {
        var (providerId, projectId, _) = await SeedAsync(linkDeleted: true);
        var seed = new SqliteCoreSeed(ServiceProvider);
        await seed.LinkAsync(projectId, await seed.ResourceAsync(providerId, ActiveResourceName));

        await using var scope = ServiceProvider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var project = await unitOfWork.GetRepository<ProjectAggregate, ProjectShortName>()
            .FindAsync(new ProjectShortName(ProjectShortName), TestContext.Current.CancellationToken);

        Assert.NotNull(project);
        Assert.Equal([ActiveResourceName], project.Resources.Select(r => r.Name.Value));
    }

    [Fact]
    public async Task Should_ReactivateUserLink_When_RemovedUserIsReassigned()
    {
        var seed = new SqliteCoreSeed(ServiceProvider);
        var projectId = await seed.ProjectAsync(ProjectShortName);
        var userId = await seed.UserAsync(RemovedMemberIdp);
        await seed.MemberAsync(projectId, userId, MemberRoleId, removed: true);

        var saveError = await ActAsync(
            (context, ct) => context.Set<UserAggregate>().SingleAsync(u => EF.Property<UserId>(u, "Id") == userId, ct),
            (project, user) => project.AssignUser(user, userId));

        Assert.Null(saveError);
        Assert.Equal([false], await ReadUserLinkStatesAsync(projectId, userId));
    }

    [Fact]
    public async Task Should_HideDeletedUserLink_When_ProjectIsLoaded()
    {
        var seed = new SqliteCoreSeed(ServiceProvider);
        var projectId = await seed.ProjectAsync(ProjectShortName);
        await seed.MemberAsync(projectId, await seed.UserAsync(RemovedMemberIdp), MemberRoleId, removed: true);
        await seed.MemberAsync(projectId, await seed.UserAsync(ActiveMemberIdp), MemberRoleId);

        await using var scope = ServiceProvider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var project = await unitOfWork.GetRepository<ProjectAggregate, ProjectShortName>()
            .FindAsync(new ProjectShortName(ProjectShortName), TestContext.Current.CancellationToken);

        Assert.NotNull(project);
        Assert.Equal([$"user-{ActiveMemberIdp}"], project.Users.Select(u => u.UserName.Value));
    }

    private async Task<(ProviderId ProviderId, ProjectId ProjectId, ProjectResourceId ResourceId)> SeedAsync(bool linkDeleted)
    {
        var seed = new SqliteCoreSeed(ServiceProvider);
        var providerId = await seed.ProviderAsync(ProviderName);
        var resourceId = await seed.ResourceAsync(providerId, ResourceName);
        var projectId = await seed.ProjectAsync(ProjectShortName);
        await seed.LinkAsync(projectId, resourceId, deleted: linkDeleted);
        return (providerId, projectId, resourceId);
    }

    private Task<Exception?> ActAsync(
        ProjectResourceId resourceId, Func<ProjectAggregate, ProjectResource, Result<Error>> change) =>
        ActAsync(
            (context, ct) => context.Set<ProjectResource>().SingleAsync(r => EF.Property<ProjectResourceId>(r, "Id") == resourceId, ct),
            change);

    /// <summary>
    /// Loads the project and the <paramref name="load"/>ed counterpart in a fresh scope, applies
    /// <paramref name="change"/> and saves.
    /// </summary>
    /// <returns>The exception the save threw, or null when it succeeded.</returns>
    private async Task<Exception?> ActAsync<TCounterpart>(
        Func<DatabaseContext, CancellationToken, Task<TCounterpart>> load,
        Func<ProjectAggregate, TCounterpart, Result<Error>> change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var project = await unitOfWork.GetRepository<ProjectAggregate, ProjectShortName>()
            .FindAsync(new ProjectShortName(ProjectShortName), ct);
        Assert.NotNull(project);
        var counterpart = await load(context, ct);

        Assert.True(change(project, counterpart).IsSuccess);
        project.ClearDomainEvents();

        return await Record.ExceptionAsync(() => unitOfWork.SaveChangesAsync(ct));
    }

    private async Task<List<bool>> ReadLinkStatesAsync(ProjectId projectId, ProjectResourceId resourceId)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await context.Set<AssignedProjectResourceToProject>()
            .IgnoreQueryFilters()
            .Where(a => EF.Property<ProjectId>(a, "ProjectId") == projectId
                && EF.Property<ProjectResourceId>(a, "ProjectResourceId") == resourceId)
            .Select(a => a.IsDeleted)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<bool>> ReadUserLinkStatesAsync(ProjectId projectId, UserId userId)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await context.Set<AssignedUserToProject>()
            .IgnoreQueryFilters()
            .Where(a => a.ProjectId == projectId && a.UserId == userId)
            .Select(a => a.IsDeleted)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
