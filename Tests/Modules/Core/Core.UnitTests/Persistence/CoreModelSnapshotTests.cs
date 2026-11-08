using Core.DataAccess;
using Core.DataAccess.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Persistence;

/// <summary>
/// The modular <see cref="DatabaseContext"/> model must match its last migration snapshot: the host throws
/// <c>PendingModelChangesWarning</c> at startup otherwise. Remapping an existing column, such as surfacing a
/// shadow foreign key as a domain property, has to stay a zero-operation diff. No database connection is
/// opened; the comparison is between the compiled model and the snapshot.
/// </summary>
public sealed class CoreModelSnapshotTests
{
    [Fact]
    public async Task Should_HaveNoPendingModelChanges_When_ComparedWithLastMigration()
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

        Assert.False(context.Database.HasPendingModelChanges(), "The DatabaseContext model differs from the last migration snapshot.");
    }
}
