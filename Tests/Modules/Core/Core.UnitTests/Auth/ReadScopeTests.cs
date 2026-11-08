using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.UnitTests.Auth;

public sealed class ReadScopeTests
{
    private static readonly WellKnownRoles Roles = new();

    private static Caller MakeCaller(
        string role,
        string? providerName = null,
        IReadOnlyDictionary<string, IReadOnlySet<ProjectShortName>>? projectRoles = null) =>
        new(
            new IdpId("u"),
            role,
            null,
            projectRoles ?? new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            providerName is null ? null : DomainProviderName.From(providerName));

    [Fact]
    public void Should_BeAdmin_When_RoleMatchesAdmin_IgnoringCase()
    {
        Assert.True(MakeCaller("aDmIn").IsAdmin(Roles));
    }

    [Fact]
    public void Should_NotBeAdmin_When_RoleIsNotAdmin()
    {
        Assert.False(MakeCaller("User").IsAdmin(Roles));
    }

    [Fact]
    public void Should_ResolveAll_When_Admin()
    {
        var scope = MakeCaller(Roles.DefaultAdminRoleName).ResolveReadScope(Roles);

        Assert.IsType<ReadScope.All>(scope);
    }

    [Fact]
    public void Should_PreferAdmin_Over_ProviderIdentity()
    {
        // Admin wins even when a providerName claim is also present — same precedence as writes.
        var scope = MakeCaller(Roles.DefaultAdminRoleName, "acme").ResolveReadScope(Roles);

        Assert.IsType<ReadScope.All>(scope);
    }

    [Fact]
    public void Should_ResolveProvider_When_NonAdmin_With_ProviderClaim()
    {
        var scope = MakeCaller("Provider", "acme").ResolveReadScope(Roles);

        var provider = Assert.IsType<ReadScope.Provider>(scope);
        Assert.Equal(DomainProviderName.From("acme"), provider.ProviderName);
    }

    [Fact]
    public void Should_ResolveScoped_When_NonAdmin_Without_ProviderClaim()
    {
        var projectRoles = new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Owner"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj-a") },
        };
        var caller = MakeCaller("User", projectRoles: projectRoles);

        var scope = caller.ResolveReadScope(Roles);

        var scoped = Assert.IsType<ReadScope.Scoped>(scope);
        Assert.Contains(ProjectShortName.Create("proj-a"), scoped.Projects);
    }

    [Fact]
    public void Should_PreferProvider_Over_CallerScope_When_NonAdminHasBoth()
    {
        // A provider identity that also carries project roles still resolves to the provider tier — the
        // project scope is not unioned in.
        var projectRoles = new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Owner"] = new HashSet<ProjectShortName> { ProjectShortName.Create("proj-a") },
        };
        var caller = MakeCaller("Provider", "acme", projectRoles);

        var scope = caller.ResolveReadScope(Roles);

        var provider = Assert.IsType<ReadScope.Provider>(scope);
        Assert.Equal(DomainProviderName.From("acme"), provider.ProviderName);
    }

    [Fact]
    public void Should_ResolveScoped_WithEmptyScope_When_CallerHasNoProjects()
    {
        // A bare user with no project roles sees nothing — Scoped over an empty set, not All.
        var scope = MakeCaller("User").ResolveReadScope(Roles);

        var scoped = Assert.IsType<ReadScope.Scoped>(scope);
        Assert.Empty(scoped.Projects);
    }
}
