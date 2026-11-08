using Core.DataAccess.Contexts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;
using RequestAggregate = Core.Domain.Request.Request;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests;

/// <summary>
/// Seeds the Core read-side graph (provider, location, resource, assignment, project, link, user, request) into
/// a <see cref="SqliteQueryHarness"/> database through the domain factories, one scope per row, and wires the
/// shadow foreign keys through the change tracker. Rows go through EF, so every stored value has exactly the
/// format the translated queries compare against.
/// </summary>
internal sealed class SqliteCoreSeed(IServiceProvider services)
{
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static int _domainTypeKey = 5000;

    /// <summary>Seeds a provider in country 1 and returns its shadow id.</summary>
    internal Task<ProviderId> ProviderAsync(string name, bool isPrivate = false, bool deactivated = false) =>
        WithContextAsync(async (context, ct) =>
        {
            var provider = ProviderAggregate.Create(ProviderName.From(name), countryId: 1, isPrivate: isPrivate);
            context.Add(provider);
            if (deactivated)
            {
                Assert.True(provider.Deactivate().IsSuccess);
            }

            await SaveAsync(context, ct);
            return (ProviderId)context.Entry(provider).Property("Id").CurrentValue!;
        });

    /// <summary>Seeds a location of <paramref name="providerId"/> and returns its shadow id.</summary>
    internal Task<Guid> LocationAsync(
        ProviderId providerId, string name, ELocationType type = ELocationType.HPC, bool deleted = false, bool isPrivate = false) =>
        WithContextAsync(async (context, ct) =>
        {
            var location = Location.Create(
                LocationName.From(name), $"https://{name}.example.org", type, isPrivate: isPrivate, description: $"{name} description");
            if (deleted)
            {
                location.Delete();
            }

            context.Add(location);
            context.Entry(location).Property("ProviderId").CurrentValue = providerId;
            await SaveAsync(context, ct);
            return (Guid)context.Entry(location).Property("Id").CurrentValue!;
        });

    /// <summary>
    /// Seeds a resource owned by <paramref name="providerId"/>; a null owner seeds a legacy standalone row, created
    /// against a throwaway provider whose foreign key is nulled afterwards.
    /// </summary>
    internal Task<ProjectResourceId> ResourceAsync(
        ProviderId? providerId,
        string name,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string? principalInvestigatorEmail = null,
        bool deleted = false) =>
        WithContextAsync(async (context, ct) =>
        {
            var owner = providerId is { } id
                ? await context.Set<ProviderAggregate>().IgnoreQueryFilters().SingleAsync(p => EF.Property<ProviderId>(p, "Id") == id, ct)
                : ProviderAggregate.Create(ProviderName.From($"standalone-{Guid.NewGuid():N}"), countryId: 1);
            if (providerId is null)
            {
                context.Add(owner);
            }

            var userIdp = IdpId.From(Guid.NewGuid().ToString());
            var user = User.CreateUser(Email.Create("no@example.org"), UserName.Create($"test-user-{Guid.NewGuid():N}"), userIdp);
            context.Add(user);
            await context.SaveChangesAsync(ct);

            var userid = await context.Users.Where(f => f.IdentityProviderId == userIdp)
                .Select(s => EF.Property<UserId>(s, "Id"))
                .FirstAsync(ct);

            var resource = ProjectResource.Create(
                ResourceName.Create(name),
                owner, userid,
                start ?? Start,
                end ?? End,
                description: $"{name} description",
                primaryInvestigatorEmail: principalInvestigatorEmail is null ? null : Email.Create(principalInvestigatorEmail));
            if (deleted)
            {
                resource.Delete();
                resource.UpdateState(EProjectResourceState.Deleted);
            }

            context.Add(resource);
            await SaveAsync(context, ct);

            if (providerId is null)
            {
                context.Entry(resource).Property("ProviderId").CurrentValue = null;
                await context.SaveChangesAsync(ct);
            }

            return (ProjectResourceId)context.Entry(resource).Property("Id").CurrentValue!;
        });

    /// <summary>Assigns <paramref name="resourceId"/> to <paramref name="locationId"/> with the given specification values.</summary>
    internal Task<Guid> AssignAsync(
        ProjectResourceId resourceId, Guid locationId, bool deleted = false, string? slug = null, params (string Name, string Value)[] values) =>
        WithContextAsync(async (context, ct) =>
        {
            var resource = await context.Set<ProjectResource>().IgnoreQueryFilters()
                .SingleAsync(pr => EF.Property<ProjectResourceId>(pr, "Id") == resourceId, ct);
            var assignment = AssignedLocation.Create(resource, allocationUnitTypeId: 1, allocationUnitCount: 10, slug: slug);
            if (deleted)
            {
                assignment.Delete();
            }

            context.Add(assignment);
            context.Entry(assignment).Property("LocationId").CurrentValue = locationId;

            foreach (var (name, value) in values)
            {
                var row = AssignedLocationValue.Create(name, value);
                context.Add(row);
                context.Entry(row).Property("AssignedLocationId").CurrentValue = assignment.Id;
            }

            await SaveAsync(context, ct);
            return assignment.Id;
        });

    /// <summary>Seeds a project with a domain type of its own and returns its shadow id.</summary>
    internal Task<ProjectId> ProjectAsync(
        string shortName, bool deleted = false, string? email = null, DateTimeOffset? start = null, DateTimeOffset? end = null) =>
        WithContextAsync(async (context, ct) =>
        {
            var project = ProjectAggregate.Create(
                new ProjectShortName(shortName),
                ProjectName.Create(shortName),
                ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
                UserId.New(),
                start ?? Start,
                end ?? End,
                ServiceName.Default,
                email: email is null ? null : Email.Create(email));
            if (deleted)
            {
                Assert.True(project.Delete().IsSuccess);
            }

            context.Add(project);
            foreach (var domainType in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
            {
                var key = domainType.Metadata.FindPrimaryKey()!.Properties[0].Name;
                domainType.Property(key).CurrentValue = new ProjectDomainTypeId(Interlocked.Increment(ref _domainTypeKey));
            }

            await SaveAsync(context, ct);
            return (ProjectId)context.Entry(project).Property("Id").CurrentValue!;
        });

    /// <summary>Links <paramref name="resourceId"/> to <paramref name="projectId"/>.</summary>
    internal Task LinkAsync(ProjectId projectId, ProjectResourceId resourceId, bool deleted = false) =>
        WithContextAsync(async (context, ct) =>
        {
            var link = AssignedProjectResourceToProject.Create();
            if (deleted)
            {
                link.Delete();
            }

            context.Add(link);
            context.Entry(link).Property("ProjectId").CurrentValue = projectId;
            context.Entry(link).Property("ProjectResourceId").CurrentValue = resourceId;
            await SaveAsync(context, ct);
            return true;
        });

    /// <summary>Seeds a user whose user name and email derive from <paramref name="idp"/>, and returns its shadow id.</summary>
    internal Task<UserId> UserAsync(string idp, bool deleted = false, ProviderId? assignedProvider = null) =>
        WithContextAsync(async (context, ct) =>
        {
            var user = UserAggregate.CreateUser(Email.Create($"{idp}@example.org"), UserName.Create($"user-{idp}"), new IdpId(idp));
            if (assignedProvider is { } providerId)
            {
                Assert.True(user.AssignProvider(providerId).IsSuccess);
            }

            if (deleted)
            {
                Assert.True(user.Deactivate().IsSuccess);
            }

            context.Add(user);
            await SaveAsync(context, ct);
            return (UserId)context.Entry(user).Property("Id").CurrentValue!;
        });

    /// <summary>
    /// Makes <paramref name="userId"/> a member of <paramref name="projectId"/> with project role <paramref name="roleId"/>;
    /// <paramref name="removed"/> soft-deletes the membership and the role row again.
    /// </summary>
    internal Task MemberAsync(ProjectId projectId, UserId userId, int roleId, bool removed = false) =>
        WithContextAsync(async (context, ct) =>
        {
            var project = await context.Set<ProjectAggregate>().IgnoreQueryFilters()
                .SingleAsync(p => EF.Property<ProjectId>(p, "Id") == projectId, ct);
            var user = await context.Set<UserAggregate>().IgnoreQueryFilters()
                .SingleAsync(u => EF.Property<UserId>(u, "Id") == userId, ct);
            Assert.True(project.AssignUserWithRole(user, UserId.New(), new ProjectRoleId(roleId)).IsSuccess);
            if (removed)
            {
                Assert.True(project.UnassignUserWithRole(user, UserId.New(), new ProjectRoleId(-1)).IsSuccess);
            }

            await SaveAsync(context, ct);
            return true;
        });

    /// <summary>Assigns a new catalogue tag named <paramref name="name"/> to <paramref name="projectId"/>.</summary>
    internal Task TagAsync(ProjectId projectId, string name, bool deleted = false) =>
        WithContextAsync(async (context, ct) =>
        {
            var project = await context.Set<ProjectAggregate>().IgnoreQueryFilters()
                .SingleAsync(p => EF.Property<ProjectId>(p, "Id") == projectId, ct);
            var tag = ProjectTag.Create(TagName.Create(name), UserName.Create("seed"));
            if (deleted)
            {
                Assert.True(tag.Delete().IsSuccess);
            }

            Assert.True(project.AssignExistingTag(tag).IsSuccess);
            await SaveAsync(context, ct);
            return true;
        });

    /// <summary>Seeds a request, moves it to <paramref name="state"/> when that is not pending, and returns its shadow id.</summary>
    internal Task<Guid> RequestAsync(
        string contactEmail,
        ProjectId projectId,
        UserId userId,
        RequestType? type = null,
        RequestState? state = null,
        ProjectResourceId? resourceId = null,
        ResourceName? resourceName = null,
        ProviderId? providerId = null,
        bool deleted = false,
        params (int Action, string Link, DateTimeOffset ValidTo, bool Deleted)[] links) =>
        WithContextAsync(async (context, ct) =>
        {
            var request = RequestAggregate.Create(
                Email.Create(contactEmail), projectId, userId, type ?? RequestType.Default, resourceId, resourceName, providerId);
            if (state is { } target && target != RequestState.Pending)
            {
                Assert.True(request.TransitionToState(target).IsSuccess);
            }

            foreach (var (action, link, validTo, linkDeleted) in links)
            {
                var added = request.AddActionLink(action, link, validTo);
                if (linkDeleted)
                {
                    added.Delete();
                }
            }

            if (deleted)
            {
                Assert.True(request.Delete().IsSuccess);
            }

            context.Add(request);
            await SaveAsync(context, ct);
            return (Guid)context.Entry(request).Property("Id").CurrentValue!;
        });

    private async Task<T> WithContextAsync<T>(Func<DatabaseContext, CancellationToken, Task<T>> seed)
    {
        await using var scope = services.CreateAsyncScope();
        return await seed(scope.ServiceProvider.GetRequiredService<DatabaseContext>(), TestContext.Current.CancellationToken);
    }

    private static async Task SaveAsync(DatabaseContext context, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added || entry.Metadata.FindProperty("CreatedAt") is null)
                continue;

            entry.Property("CreatedAt").CurrentValue = entry.Property("CreatedAt").CurrentValue switch
            {
                DateTime { Ticks: 0 } => now.UtcDateTime,
                DateTimeOffset { Ticks: 0 } => now,
                var current => current,
            };
        }

        await context.SaveChangesAsync(ct);
    }
}
