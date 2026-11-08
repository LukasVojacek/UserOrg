using Core.DataAccess;
using Core.DataAccess.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Persistence;

/// <summary>
/// A required child collection of the v2 model (a required foreign key whose principal holds a collection
/// navigation to the dependent) must map <see cref="DeleteBehavior.ClientCascade"/> or
/// <see cref="DeleteBehavior.Cascade"/>: with <see cref="DeleteBehavior.NoAction"/> a child dropped from the
/// collection severs the relationship and <c>SaveChangesAsync</c> throws. <c>User._systemPermissions</c> is the
/// one exception, its single row is changed in place. No database connection is opened; the assertion is over
/// the compiled model.
/// </summary>
public sealed class RequiredCollectionDeleteBehaviorTests
{
    private static readonly (string Principal, string Navigation) SystemPermissions = ("User", "_systemPermissions");

    [Fact]
    public async Task Should_DiscoverEveryKnownRequiredChildCollection_When_WalkingTheModel()
    {
        var collections = await RequiredChildCollectionsAsync();

        var discovered = collections.Select(c => (c.Principal, c.Navigation)).ToHashSet();
        (string, string)[] expected =
        [
            ("AssignedLocation", "_assignedLocationValues"),
            ("Location", "_locationAllocations"),
            ("Provider", "_providerLocations"),
            ("Project", "_assignedUsers"),
            ("Project", "_assignedResources"),
            ("Request", "_requestActionLinks"),
            SystemPermissions,
        ];
        Assert.All(expected, e => Assert.Contains(e, discovered));
    }

    [Fact]
    public async Task Should_MapClientCascadeOrCascade_When_RequiredChildCollection()
    {
        var collections = await RequiredChildCollectionsAsync();

        var offenders = collections
            .Where(c => (c.Principal, c.Navigation) != SystemPermissions)
            .Where(c => c.DeleteBehavior is not (DeleteBehavior.ClientCascade or DeleteBehavior.Cascade))
            .Select(c => $"{c.Principal}.{c.Navigation} -> {c.Dependent}: {c.DeleteBehavior}")
            .ToArray();
        Assert.True(offenders.Length == 0, $"Required child collections without a cascade:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public async Task Should_KeepNoAction_When_SystemPermissions()
    {
        var collections = await RequiredChildCollectionsAsync();

        var systemPermissions = Assert.Single(collections, c => (c.Principal, c.Navigation) == SystemPermissions);
        Assert.Equal(DeleteBehavior.NoAction, systemPermissions.DeleteBehavior);
    }

    private static async Task<List<(string Principal, string Navigation, string Dependent, DeleteBehavior DeleteBehavior)>> RequiredChildCollectionsAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UserOrgDBContext"] = IntegrationDb.ConnectionStringOrPlaceholder,
            })
            .Build();

        var services = IntegrationDb.NewServices();
        services.AddCoreDataAccess(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return context.Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredForeignKeys())
            .Where(fk => fk.IsRequired && fk.PrincipalToDependent is { IsCollection: true })
            .Select(fk => (fk.PrincipalEntityType.ClrType.Name, fk.PrincipalToDependent!.Name, fk.DeclaringEntityType.ClrType.Name, fk.DeleteBehavior))
            .ToList();
    }
}