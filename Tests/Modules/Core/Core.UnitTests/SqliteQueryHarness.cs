using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests;

/// <summary>
/// Base for tests that run the real <see cref="DatabaseContext"/> model on a fresh SQLite in-memory connection, so
/// queries execute as translated SQL. Foreign keys are off because the configurations declare required FKs to
/// tables a test does not seed, the same thing EF InMemory does implicitly. The generated create script drops
/// <c>(max)</c>, which SQLite's grammar rejects for a parenthesised type, and maps <c>SYSUTCDATETIME()</c> to
/// <c>CURRENT_TIMESTAMP</c>; the rest of the real schema stays untouched.
/// </summary>
public abstract class SqliteQueryHarness : IAsyncLifetime
{
    /// <summary>The open in-memory connection the schema lives on.</summary>
    protected SqliteConnection Connection { get; private set; } = null!;

    /// <summary>The container the test resolves its query and <see cref="DatabaseContext"/> scopes from.</summary>
    protected ServiceProvider ServiceProvider { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        await Connection.OpenAsync();

        await using (var pragma = Connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = OFF;";
            await pragma.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseSqlite(Connection));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        AddServices(services);

        ServiceProvider = services.BuildServiceProvider();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var script = context.Database.GenerateCreateScript()
            .Replace("(max)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("SYSUTCDATETIME()", "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase);
        await context.Database.ExecuteSqlRawAsync(script);
        await SeedAsync(context);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the container, then the connection the schema lives on.</summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        await ServiceProvider.DisposeAsync();
        await Connection.DisposeAsync();
    }

    /// <summary>Registers the query under test.</summary>
    protected virtual void AddServices(IServiceCollection services)
    {
    }

    /// <summary>Seeds the lookup rows every test of the class needs, once the schema exists.</summary>
    private protected virtual Task SeedAsync(DatabaseContext context) => Task.CompletedTask;
}
