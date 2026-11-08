using Microsoft.EntityFrameworkCore;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// <see cref="ProjectRepository.GetAllBySpecificationsAsync"/> under a <c>providerId</c> scope — a project is
/// visible when its resource-assignment chain (Project -&gt; AssignedProjectResourceToProject -&gt;
/// ProjectResource -&gt; AssignedLocation -&gt; Location) resolves to that provider, mirroring the traversal
/// <see cref="ProjectRepository.GetProjectsForProvider"/> uses, <em>or</em> when the caller identified by
/// <c>callerIdpUserId</c> created it or holds a non-deleted assignment to it — all on top of the existing
/// filter chain.
/// </summary>
public sealed class ProjectRepositoryProviderScopeTests
{
    // Shared lookup rows for the whole test (one instance of the test class per test method, per xUnit's
    // default lifecycle): every entity built below points at the same Country/LocationType/AllocationUnitType/
    // ProjectDomainType row, so the in-memory change tracker never sees two distinct instances with the same key.
    private readonly Country _country = new() { Id = 1, Name = "CZ" };
    private readonly LocationType _hpc = new() { Id = 1, Name = "HPC" };
    private readonly AllocationUnitType _coreHours = new() { Id = 1, Name = "CoreHours" };
    private readonly ProjectDomainType _domain = new() { Id = 1, Name = "Domain" };

    private static DatabaseContext BuildContext() => new(
        new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private Provider BuildProvider() => new() { Name = "Provider", Country = _country };

    private Location BuildLocation(Provider provider) => new()
    {
        Name = "Location",
        Endpoint = "https://location.example",
        Provider = provider,
        LocationType = _hpc
    };

    /// <summary>
    /// Links a new <see cref="ProjectResource"/> to <paramref name="location"/> via an <see cref="AssignedLocation"/>,
    /// wiring both sides of the navigation so the graph is reachable regardless of which end EF Core walks.
    /// </summary>
    private ProjectResource ResourceLinkedTo(Location location, bool linkDeleted = false, bool expired = false)
    {
        var resource = new ProjectResource
        {
            Name = $"Resource-{Guid.NewGuid()}",
            StartDate = DateTime.UtcNow.AddDays(-365),
            EndDate = expired ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow.AddDays(30)
        };
        var link = new AssignedLocation
        {
            ProjectResource = resource,
            Location = location,
            AllocationUnitType = _coreHours,
            IsDeleted = linkDeleted
        };
        resource.AssignedLocations.Add(link);
        location.AssignedLocations.Add(link);
        return resource;
    }

    /// <summary>
    /// Builds a project assigned to <paramref name="resource"/> via an <see cref="AssignedProjectResourceToProject"/>,
    /// wiring both sides of the navigation.
    /// </summary>
    private Project ProjectWithResource(string shortName, ProjectResource resource, bool assignmentDeleted = false, bool expired = false)
    {
        var project = ProjectWithoutResource(shortName, expired: expired);
        var assignment = new AssignedProjectResourceToProject
        {
            Project = project,
            ProjectResource = resource,
            IsDeleted = assignmentDeleted
        };
        project.AssignedProjectResources.Add(assignment);
        return project;
    }

    /// <summary>
    /// Builds a project with no resource assignments, created by <paramref name="createdByUser"/> (a fresh
    /// <see cref="User"/> when omitted).
    /// </summary>
    private Project ProjectWithoutResource(string shortName, User? createdByUser = null, bool expired = false) => new()
    {
        ShortName = new ProjectShortName(shortName),
        Name = shortName,
        ProjectDomainType = _domain,
        CreatedByUser = createdByUser ?? new User { Email = "owner@example.com" },
        StartDate = DateTime.UtcNow.AddDays(-365),
        EndDate = expired ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow.AddDays(30)
    };

    /// <summary>
    /// Links <paramref name="user"/> to <paramref name="project"/> via an <see cref="AssignedUserToProject"/>,
    /// wiring both sides of the navigation.
    /// </summary>
    private static void AssignUserToProject(Project project, User user, bool assignmentDeleted = false)
    {
        var assignment = new AssignedUserToProject { Project = project, User = user, IsDeleted = assignmentDeleted };
        project.AssignedUsers.Add(assignment);
        user.AssignedToProjects.Add(assignment);
    }

    [Fact(DisplayName = "Only the project whose resource chain resolves to the requested provider is returned")]
    public async Task ReturnsOnlyProjectsLinkedToRequestedProvider()
    {
        using var context = BuildContext();

        var providerA = BuildProvider();
        var providerB = BuildProvider();
        var locationA = BuildLocation(providerA);
        var locationB = BuildLocation(providerB);

        var projectOnA = ProjectWithResource("ProjA", ResourceLinkedTo(locationA));
        var projectOnB = ProjectWithResource("ProjB", ResourceLinkedTo(locationB));

        context.Projects.AddRange(projectOnA, projectOnB);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, providerA.Id, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(projectOnA.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project reachable only through a soft-deleted AssignedProjectResourceToProject stays in the provider scope")]
    public async Task ReturnsProject_WhenResourceAssignmentIsSoftDeleted()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);
        var project = ProjectWithResource("ProjDeletedAssignment", ResourceLinkedTo(location), assignmentDeleted: true);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project reachable only through a soft-deleted AssignedLocation stays in the provider scope")]
    public async Task ReturnsProject_WhenAssignedLocationIsSoftDeleted()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);
        var project = ProjectWithResource("ProjDeletedLocation", ResourceLinkedTo(location, linkDeleted: true));

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project whose own validity has expired stays in the provider scope")]
    public async Task ReturnsExpiredProject()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);
        var project = ProjectWithResource("ProjExpired", ResourceLinkedTo(location), expired: true);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project whose every assigned resource has expired stays in the provider scope")]
    public async Task ReturnsProject_WhenAllAssignedResourcesExpired()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);

        // Two resources, both past their EndDate, one of them also flagged by the sync as terminated+archived.
        var project = ProjectWithResource("ProjAllResourcesExpired", ResourceLinkedTo(location, expired: true));
        var secondResource = ResourceLinkedTo(location, linkDeleted: true, expired: true);
        secondResource.IsDeleted = true;
        project.AssignedProjectResources.Add(new AssignedProjectResourceToProject
        {
            Project = project,
            ProjectResource = secondResource
        });

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A soft-deleted project of the provider is excluded")]
    public async Task ExcludesSoftDeletedProject()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);
        var project = ProjectWithResource("ProjDeleted", ResourceLinkedTo(location));
        project.IsDeleted = true;

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact(DisplayName = "The provider scope narrows further on top of the existing ShortName filter")]
    public async Task ComposesWithShortNameFilter()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);

        var matching = ProjectWithResource("Match-01", ResourceLinkedTo(location));
        var sameProviderDifferentName = ProjectWithResource("Other-02", ResourceLinkedTo(location));

        context.Projects.AddRange(matching, sameProviderDifferentName);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync("Match-01", null, null, null, null, provider.Id, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(matching.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project the caller is assigned to but that has no resources at all is returned")]
    public async Task ReturnsProject_WhenCallerIsAssignedButProjectHasNoResources()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var caller = new User { Email = "caller@example.com" };
        var project = ProjectWithoutResource("NoResources");
        AssignUserToProject(project, caller);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project the caller is assigned to is returned even when its resources belong to a different provider")]
    public async Task ReturnsProject_WhenCallerIsAssignedButResourceBelongsToDifferentProvider()
    {
        using var context = BuildContext();

        var callerProvider = BuildProvider();
        var otherProvider = BuildProvider();
        var otherLocation = BuildLocation(otherProvider);

        var caller = new User { Email = "caller@example.com" };
        var project = ProjectWithResource("OtherProviderResource", ResourceLinkedTo(otherLocation));
        AssignUserToProject(project, caller);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, callerProvider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project the caller created but holds no assignment row for is returned")]
    public async Task ReturnsProject_WhenCallerCreatedItButIsNotAssigned()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var caller = new User { Email = "caller@example.com" };
        var project = ProjectWithoutResource("CreatedByCaller", createdByUser: caller);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project that is both provider-scoped and a caller membership appears exactly once")]
    public async Task ReturnsProjectOnce_WhenBothProviderScopedAndCallerMember()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var location = BuildLocation(provider);
        var caller = new User { Email = "caller@example.com" };
        var project = ProjectWithResource("BothScopes", ResourceLinkedTo(location));
        AssignUserToProject(project, caller);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }

    [Fact(DisplayName = "A project with neither a provider-scoped resource nor a caller membership is excluded")]
    public async Task ExcludesProject_WithNeitherProviderScopeNorCallerMembership()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var otherProvider = BuildProvider();
        var otherLocation = BuildLocation(otherProvider);
        var caller = new User { Email = "caller@example.com" };

        var project = ProjectWithResource("Unrelated", ResourceLinkedTo(otherLocation));

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Empty(result);
    }

    [Fact(DisplayName = "A project whose only tie to the caller is a soft-deleted assignment is excluded")]
    public async Task ExcludesProject_WhenTheCallerAssignmentIsSoftDeleted()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var caller = new User { Email = "caller@example.com" };
        var project = ProjectWithoutResource("RemovedMember");
        AssignUserToProject(project, caller, assignmentDeleted: true);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Empty(result);
    }

    /// <summary>
    /// The empty case is the one that bites: <c>IdentityProviderUserId</c> is NOT NULL but not non-empty, so a
    /// user carrying an empty id is representable and an unguarded equality would hand its projects to any
    /// caller whose <c>sub</c> claim came through empty. The null case cannot be falsified by any schema-valid
    /// arrangement (the column and the CreatedByUser FK are both required) and only documents the contract.
    /// </summary>
    [Theory(DisplayName = "A null or empty callerIdpUserId does not widen the provider scope with a membership match")]
    [InlineData(null)]
    [InlineData("")]
    public async Task DoesNotWiden_WhenCallerIdpUserIdIsNullOrEmpty(string? callerIdpUserId)
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var member = new User { Email = "member@example.com" };
        var project = ProjectWithoutResource("MemberOnly");
        AssignUserToProject(project, member);

        var emptyIdUser = new User { Email = "empty-id@example.com", IdentityProviderUserId = "" };
        var emptyIdOwnedProject = ProjectWithoutResource("OwnedByEmptyId", createdByUser: emptyIdUser);
        var emptyIdMemberProject = ProjectWithoutResource("MemberWithEmptyId");
        AssignUserToProject(emptyIdMemberProject, emptyIdUser);

        context.Projects.AddRange(project, emptyIdOwnedProject, emptyIdMemberProject);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync(null, null, null, null, null, provider.Id, CancellationToken.None, callerIdpUserId);

        Assert.Empty(result);
    }

    [Fact(DisplayName = "The ShortName filter finds a caller's member project even when it is outside the provider's scope")]
    public async Task ComposesWithShortNameFilter_ForMemberProjectOutsideProviderScope()
    {
        using var context = BuildContext();

        var provider = BuildProvider();
        var otherProvider = BuildProvider();
        var otherLocation = BuildLocation(otherProvider);
        var caller = new User { Email = "caller@example.com" };

        var project = ProjectWithResource("Member-Outside-Provider", ResourceLinkedTo(otherLocation));
        AssignUserToProject(project, caller);

        context.Projects.Add(project);
        await context.SaveChangesAsync(CancellationToken.None);

        var repository = new ProjectRepository(context);

        var result = await repository.GetAllBySpecificationsAsync("Member-Outside-Provider", null, null, null, null, provider.Id, CancellationToken.None, caller.IdentityProviderUserId);

        Assert.Single(result);
        Assert.Equal(project.Id, result[0].Id);
    }
}
