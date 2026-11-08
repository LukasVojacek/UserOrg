using Core.DataAccess.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Verifies that SQL command-logging events are suppressed outside Development on <see cref="OutboxPersistenceExtensions.AddOutboxPersistence"/>:
/// ensures <see cref="RelationalEventId.CommandExecuting"/> and <see cref="RelationalEventId.CommandExecuted"/> are ignored in production,
/// left enabled in Development, and <see cref="RelationalEventId.CommandError"/> is never ignored.
/// </summary>
public sealed class OutboxPersistenceExtensionsTests
{
    private static HostApplicationBuilder CreateBuilder(string environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration["ConnectionStrings:UserOrgDBContext"] =
            "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";
        return builder;
    }

    private static WarningsConfiguration ResolveWarnings(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<OutboxDbContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!.WarningsConfiguration;
    }

    [Fact(DisplayName = "AddOutboxPersistence suppresses SQL command events outside Development")]
    public void AddOutboxPersistence_OutsideDevelopment_SuppressesCommandEvents()
    {
        var builder = CreateBuilder(Environments.Production);

        builder.AddOutboxPersistence();

        var warnings = ResolveWarnings(builder);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddOutboxPersistence leaves SQL command events enabled in Development")]
    public void AddOutboxPersistence_InDevelopment_LeavesCommandEventsEnabled()
    {
        var builder = CreateBuilder(Environments.Development);

        builder.AddOutboxPersistence();

        var warnings = ResolveWarnings(builder);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }
}
