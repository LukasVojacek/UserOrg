using Core.DataAccess;
using Core.DataAccess.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Moq;

namespace Core.UnitTests.Persistence;

/// <summary>
/// Verifies that SQL command-logging events are suppressed outside Development on
/// <see cref="ServiceCollectionExtensions.AddCoreDataAccess"/>: ensures <see cref="RelationalEventId.CommandExecuting"/>
/// and <see cref="RelationalEventId.CommandExecuted"/> are ignored in production, left enabled in Development,
/// and <see cref="RelationalEventId.CommandError"/> is never ignored.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    private const string ConnectionString =
        "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new("ConnectionStrings:UserOrgDBContext", ConnectionString)])
            .Build();

    private static ServiceCollection ServicesFor(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton(environment.Object);
        return services;
    }

    private static WarningsConfiguration ResolveWarnings(ServiceCollection services) =>
        ResolveCoreOptions(services).WarningsConfiguration;

    private static CoreOptionsExtension ResolveCoreOptions(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<DatabaseContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!;
    }

    [Fact(DisplayName = "AddCoreDataAccess suppresses SQL command events outside Development")]
    public void AddCoreDataAccess_OutsideDevelopment_SuppressesCommandEvents()
    {
        var services = ServicesFor(Environments.Production);

        services.AddCoreDataAccess(Configuration());

        var warnings = ResolveWarnings(services);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddCoreDataAccess leaves SQL command events enabled in Development")]
    public void AddCoreDataAccess_InDevelopment_LeavesCommandEventsEnabled()
    {
        var services = ServicesFor(Environments.Development);

        services.AddCoreDataAccess(Configuration());

        var warnings = ResolveWarnings(services);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }

    [Fact(DisplayName = "AddCoreDataAccess does not enable sensitive data logging outside Development")]
    public void AddCoreDataAccess_OutsideDevelopment_DoesNotEnableSensitiveDataLogging()
    {
        var services = ServicesFor(Environments.Production);

        services.AddCoreDataAccess(Configuration());

        var core = ResolveCoreOptions(services);
        Assert.False(core.IsSensitiveDataLoggingEnabled);
    }

    [Fact(DisplayName = "AddCoreDataAccess enables sensitive data logging in Development")]
    public void AddCoreDataAccess_InDevelopment_EnablesSensitiveDataLogging()
    {
        var services = ServicesFor(Environments.Development);

        services.AddCoreDataAccess(Configuration());

        var core = ResolveCoreOptions(services);
        Assert.True(core.IsSensitiveDataLoggingEnabled);
    }
}
