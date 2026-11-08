using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

/// <summary>
/// Database context factory
/// </summary>
public class DatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    public DatabaseContextFactory()
    {

    }
    #endregion
    #region Methods
    /// <summary>
    /// Create database context
    /// </summary>
    public DatabaseContext CreateDbContext(string[] args)
    {

        IConfigurationBuilder configBuilder = new ConfigurationBuilder()
            .AddUserSecrets(typeof(DatabaseContextFactory).Assembly)
            .AddEnvironmentVariables();

        var appSettingsPath = Path.GetFullPath("../API/appsettings.json");
        var connectionString = "";
        var fileExists = File.Exists(appSettingsPath);
        if (fileExists)
        {
            configBuilder.AddJsonFile(appSettingsPath);
        }

        IConfigurationRoot configs = configBuilder.Build();

        var optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>();
        if (fileExists)
        {
            connectionString = configs.GetConnectionString("UserOrgDBContext");
        }

        optionsBuilder.UseSqlServer(connectionString);

        return new DatabaseContext(optionsBuilder.Options);
    }
    #endregion
}