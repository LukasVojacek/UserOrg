using Heappe.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Heappe.UnitTests.Persistence;

/// <summary>
/// Characterization tests for <see cref="HeappeIntegrationPersistenceExtensions.AddHeappeIntegrationPersistence"/>,
/// pinning today's DI registrations before the SQL-command-logging toggle (issue #241) touches this call site.
/// </summary>
public sealed class HeappeIntegrationPersistenceExtensionsTests
{
    private static HostApplicationBuilder CreateBuilder() => CreateBuilder(Environments.Production);

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
        var options = provider.GetRequiredService<DbContextOptions<HeappeIntegrationDbContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!.WarningsConfiguration;
    }

    [Fact(DisplayName = "AddHeappeIntegrationPersistence registers pooled HeappeIntegrationDbContext factory as singleton")]
    public void AddHeappeIntegrationPersistence_RegistersDbContextFactory_AsSingleton()
    {
        var builder = CreateBuilder();

        builder.AddHeappeIntegrationPersistence();

        var descriptor = builder.Services.SingleOrDefault(d => d.ServiceType == typeof(IDbContextFactory<HeappeIntegrationDbContext>));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact(DisplayName = "AddHeappeIntegrationPersistence registers HeappeMigrationStartupService as a hosted service")]
    public void AddHeappeIntegrationPersistence_RegistersMigrationStartupService_AsHostedService()
    {
        var builder = CreateBuilder();

        builder.AddHeappeIntegrationPersistence();

        var hostedServiceImplementations = builder.Services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Contains(typeof(HeappeMigrationStartupService), hostedServiceImplementations);
    }

    [Fact(DisplayName = "AddHeappeIntegrationPersistence returns the same builder instance")]
    public void AddHeappeIntegrationPersistence_ReturnsSameBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.AddHeappeIntegrationPersistence();

        Assert.Same(builder, result);
    }

    [Fact(DisplayName = "AddHeappeIntegrationPersistence suppresses SQL command events outside Development")]
    public void AddHeappeIntegrationPersistence_OutsideDevelopment_SuppressesCommandEvents()
    {
        var builder = CreateBuilder(Environments.Production);

        builder.AddHeappeIntegrationPersistence();

        var warnings = ResolveWarnings(builder);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddHeappeIntegrationPersistence leaves SQL command events enabled in Development")]
    public void AddHeappeIntegrationPersistence_InDevelopment_LeavesCommandEventsEnabled()
    {
        var builder = CreateBuilder(Environments.Development);

        builder.AddHeappeIntegrationPersistence();

        var warnings = ResolveWarnings(builder);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }
}
