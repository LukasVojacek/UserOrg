using Core.DataAccess;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Core.UnitTests;

/// <summary>
/// Regression guard: <see cref="WellKnownRoles"/> must be configured by <c>AddCoreDataAccess</c>
/// rather than by <c>Core.Composition.AddCore</c>. <c>DatabaseContext</c> (an always-registered
/// dependency of <c>AddCoreDataAccess</c>) consumes <see cref="IOptions{TOptions}"/> of
/// <see cref="WellKnownRoles"/> for role lookups. <see cref="IOptions{TOptions}"/> silently falls
/// back to type defaults when unbound instead of throwing, so an incorrect binding location would
/// corrupt role lookups without any startup failure.
/// </summary>
public sealed class CoreDataAccessCompositionTests
{
    [Fact]
    public void Should_ResolveConfiguredWellKnownRoles_When_AddCoreDataAccessComposedAlone()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = "Server=(local);Database=SmokeTest;Trusted_Connection=True;",
                ["DefaultAdminRoleName"] = "SuperAdmin"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCoreDataAccess(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var roles = serviceProvider.GetRequiredService<IOptions<WellKnownRoles>>().Value;

        Assert.Equal("SuperAdmin", roles.DefaultAdminRoleName);
    }
}
