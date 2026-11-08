using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Infrastructure.DataAccess;

public static class DataAccessExtensions
{
    /// <summary>
    /// Registers <see cref="DatabaseContext"/> and the startup migration check/apply service.
    /// </summary>
    /// <param name="services">Service collection to register into.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="sensitiveDataLogging">
    /// If <c>true</c>, enables EF sensitive data logging (e.g. parameter values in SQL).
    /// Should only be enabled in Development; use with caution as it may log PII.
    /// </param>
    /// <param name="logSqlCommands">
    /// If <c>false</c>, EF's executed-command log events are suppressed at the source, so no
    /// logger — and no step-up in log level — can emit the SQL text. Pass
    /// <c>IsDevelopment()</c>; see ADR-0076 and issue #241.
    /// </param>
    /// <returns>The modified service collection.</returns>
    public static IServiceCollection AddUserOrgDataAccess(
        this IServiceCollection services,
        string? connectionString,
        bool sensitiveDataLogging,
        bool logSqlCommands)
    {
        services.AddDbContext<IUserOrgDbContext, DatabaseContext>(options =>
        {
            options.EnableSensitiveDataLogging(sensitiveDataLogging);
            options.ConfigureSqlCommandLogging(logSqlCommands);
            options.UseSqlServer(connectionString);
        });
        services.AddTransient<DatabaseSeedService>();
        // Migration ownership moved to the modular Core.DataAccess context (ADR-0049); the seed
        // runs against a schema the modular startup migrator / DatabaseMigrations tool has applied.
        services.AddHostedService<DatabaseSeedHostedService>();
        return services;
    }
}