using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Heappe.Persistence;

internal class HeappeIntegrationDbContextFactory : IDesignTimeDbContextFactory<HeappeIntegrationDbContext>
{

    /// <summary>
    /// Constructor
    /// </summary>
    public HeappeIntegrationDbContextFactory()
    {

    }

    /// <summary>
    /// Create database context
    /// </summary>
    public HeappeIntegrationDbContext CreateDbContext(string[] args)
    {
        IConfigurationBuilder configBuilder = new ConfigurationBuilder()
            .AddUserSecrets(typeof(HeappeIntegrationDbContextFactory).Assembly)
            .AddEnvironmentVariables();

        var optionsBuilder = new DbContextOptionsBuilder<HeappeIntegrationDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings_HeappeIntegrationDbContext");

        optionsBuilder.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", HeappeIntegrationDbContext.SchemaName);
        });

        return new HeappeIntegrationDbContext(optionsBuilder.Options);
    }
}