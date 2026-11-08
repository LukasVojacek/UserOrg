using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Infrastructure.DataAccess;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Characterization tests for <see cref="DataAccessExtensions.AddUserOrgDataAccess"/>, pinning today's
/// DI registrations before the SQL-command-logging toggle (issue #241) adds a new parameter here.
/// </summary>
public sealed class DataAccessExtensionsTests
{
    private const string _connectionString = "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact(DisplayName = "AddUserOrgDataAccess registers DatabaseContext as scoped IUserOrgDbContext")]
    public void AddUserOrgDataAccess_RegistersDatabaseContext_AsScoped()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: true);

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IUserOrgDbContext));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        Assert.Equal(typeof(DatabaseContext), descriptor.ImplementationType);
    }

    [Fact(DisplayName = "AddUserOrgDataAccess registers DatabaseSeedService as transient")]
    public void AddUserOrgDataAccess_RegistersDatabaseSeedService_AsTransient()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: true);

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DatabaseSeedService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Transient, descriptor!.Lifetime);
    }

    [Fact(DisplayName = "AddUserOrgDataAccess registers the seed hosted service (migration ownership moved to modular Core.DataAccess, ADR-0049)")]
    public void AddUserOrgDataAccess_RegistersSeedHostedService()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: true);

        var hostedServiceImplementationNames = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name)
            .ToList();

        // Migration ownership moved to the modular Core.DataAccess startup migrator (ADR-0049);
        // this extension no longer registers DatabaseMigrationStartupService.
        Assert.Equal(
            ["DatabaseSeedHostedService"],
            hostedServiceImplementationNames);
    }

    [Fact(DisplayName = "AddUserOrgDataAccess returns the same service collection instance")]
    public void AddUserOrgDataAccess_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: true);

        Assert.Same(services, result);
    }

    [Fact(DisplayName = "AddUserOrgDataAccess with logSqlCommands: false suppresses the SQL command events at the EF source")]
    public void AddUserOrgDataAccess_WhenLogSqlCommandsDisabled_SuppressesCommandEvents()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: false);

        var warnings = ResolveWarnings(services);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddUserOrgDataAccess with logSqlCommands: true leaves the SQL command events enabled")]
    public void AddUserOrgDataAccess_WhenLogSqlCommandsEnabled_DoesNotSuppressCommandEvents()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: true);

        var warnings = ResolveWarnings(services);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }

    [Fact(DisplayName = "AddUserOrgDataAccess with sensitiveDataLogging: false does not enable sensitive data logging")]
    public void AddUserOrgDataAccess_WhenSensitiveDataLoggingDisabled_DoesNotEnableSensitiveDataLogging()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: false, logSqlCommands: true);

        var core = ResolveCoreOptions(services);
        Assert.False(core.IsSensitiveDataLoggingEnabled);
    }

    [Fact(DisplayName = "AddUserOrgDataAccess with sensitiveDataLogging: true enables sensitive data logging")]
    public void AddUserOrgDataAccess_WhenSensitiveDataLoggingEnabled_EnablesSensitiveDataLogging()
    {
        var services = new ServiceCollection();

        services.AddUserOrgDataAccess(_connectionString, sensitiveDataLogging: true, logSqlCommands: true);

        var core = ResolveCoreOptions(services);
        Assert.True(core.IsSensitiveDataLoggingEnabled);
    }

    private static WarningsConfiguration ResolveWarnings(IServiceCollection services) =>
        ResolveCoreOptions(services).WarningsConfiguration;

    private static CoreOptionsExtension ResolveCoreOptions(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<DatabaseContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!;
    }
}
