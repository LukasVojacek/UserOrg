using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.Persistance;
using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Heappe.Persistence;
/// <summary>
/// Provides extension methods for configuring the persistence layer required for integrating with Heappe within an
/// application's service collection.
/// </summary>
/// <remarks>This static class contains methods to register and configure the Entity Framework Core database
/// context and related persistence services necessary for the Heappe integration module. These methods are intended to
/// be used during application startup to ensure that the required persistence infrastructure is properly set
/// up.</remarks>
internal static class HeappeIntegrationPersistenceExtensions
{
    /// <summary>
    /// Represents the name of the application used for integration with Heappe.
    /// </summary>
    private const string ApplicationName = "UserOrg.HeappeIntegrationModule";
    /// <summary>
    /// Gets the default timeout duration, in seconds, for command execution.
    /// </summary>
    private const int CommandTimeOutSeconds = 30;
    /// <summary>
    /// Gets the delay, in seconds, before retrying a failed operation.
    /// </summary>
    /// <remarks>This constant is used to define the wait time between retry attempts for operations that may
    /// fail temporarily. Adjusting this value can impact the responsiveness of the application during error
    /// recovery.</remarks>
    private const int RetryDelayInSeconds = 30;

    /// <summary>
    /// Configures the persistence layer for the Heappe integration by adding a pooled DbContext factory to the service
    /// collection.
    /// </summary>
    /// <remarks>This method sets up a SQL Server database context with specific options, including connection
    /// string configuration, migration assembly, and retry policies for transient failures.</remarks>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> instance used to configure the application's services.</param>
    /// <returns>The updated <see cref="IHostApplicationBuilder"/> instance to allow for method chaining.</returns>
    internal static IHostApplicationBuilder AddHeappeIntegrationPersistence(this IHostApplicationBuilder builder)
    {

        var connectionString = builder.Configuration.GetConnectionString("UserOrgDBContext");
        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName
        };
        builder.Services.AddPooledDbContextFactory<HeappeIntegrationDbContext>(opt =>
        {
            opt.ConfigureSqlCommandLogging(builder.Environment.IsDevelopment());
            opt.UseSqlServer(connectionStringBuilder.ConnectionString, sqlOpt =>
            {
                sqlOpt.MigrationsAssembly(typeof(HeappeIntegrationDbContext).Assembly);
                sqlOpt.MigrationsHistoryTable("__EFMigrationsHistory", HeappeIntegrationDbContext.SchemaName);
                sqlOpt.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(RetryDelayInSeconds),
                    errorNumbersToAdd: null);
                sqlOpt.CommandTimeout(CommandTimeOutSeconds);
            })
            .UseAsyncSeeding((ctx, _, ct) => SeedAsync(ctx, ct));
        });

        builder.EnrichSqlServerDbContext<HeappeIntegrationDbContext>(settings => settings.DisableTracing = true);

        builder.Services.AddHostedService<HeappeMigrationStartupService>();

        return builder;
    }

    /// <summary>
    /// Seeds the default command templates and their parameters if no templates are present yet.
    /// </summary>
    internal static async Task SeedAsync(DbContext ctx, CancellationToken ct)
    {
        if (await ctx.Set<CommandTemplateDefinition>().AnyAsync(ct))
        {
            return;
        }

        var defaultHPCTemplates = GetDefaultHPCCommandTemplates();
        ctx.AddRange(defaultHPCTemplates);
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Creates a collection of predefined HPC command template location definitions for the LEXIS project.
    /// </summary>
    /// <returns>A list containing five default command templates: JobScript, Container, BulkCopy, TestTemplate, and
    /// GenericCommandTemplate, each configured for HPC execution with their respective parameters and scripts.</returns>
    private static IList<CommandTemplateLocationDefinition> GetDefaultHPCCommandTemplates()
    {
        var jobScript = new CommandTemplateLocationDefinition()
        {
            PreparationCommand = default,
            ExecutionCommand = "jobscript.sh",
            LocationType = ELocationType.HPC,
            CommandTemplateDefinition = new CommandTemplateDefinition
            {
                Name = "JobScript",
                Description = "JobScript for the LEXIS project",
                Type = ECommandTemplateType.SystemDefined,
                ComandTemplateParameterDefinitions = new List<CommandTemplateParameterDefinition>
                {
                    new()
                    {
                        Name = "fileName",
                        Description = default
                    }
                }
            },
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        var containerScript = new CommandTemplateLocationDefinition()
        {
            PreparationCommand = default,
            ExecutionCommand = "container.sh",
            LocationType = ELocationType.HPC,
            CommandTemplateDefinition = new CommandTemplateDefinition
            {
                Name = "Container",
                Description = "Container for the LEXIS project",
                Type = ECommandTemplateType.SystemDefined,
                ComandTemplateParameterDefinitions = new List<CommandTemplateParameterDefinition>
                {
                    new()
                    {
                        Name = "parameters",
                        Description = default
                    }
                }
            },
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        var bulkCopyScript = new CommandTemplateLocationDefinition()
        {
            PreparationCommand = default,
            ExecutionCommand = "bulk_copy.sh",
            LocationType = ELocationType.HPC,
            CommandTemplateDefinition = new CommandTemplateDefinition
            {
                Name = "BulkCopy",
                Description = "Bulk copy for the LEXIS project",
                Type = ECommandTemplateType.SystemDefined,
                ComandTemplateParameterDefinitions = new List<CommandTemplateParameterDefinition>
                {
                    new()
                    {
                        Name = "sourceDir",
                        Description = default
                    },
                    new()
                    {
                        Name = "targetDir",
                        Description = default
                    }
                }
            },
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        var testTemplateScript = new CommandTemplateLocationDefinition()
        {
            PreparationCommand = default,
            ExecutionCommand = "test.sh",
            LocationType = ELocationType.HPC,
            CommandTemplateDefinition = new CommandTemplateDefinition
            {
                Name = "TestTemplate",
                Description = "Test template script",
                Type = ECommandTemplateType.UserDefined,
                ComandTemplateParameterDefinitions = new List<CommandTemplateParameterDefinition>
                {
                    new()
                    {
                        Name = "inputParam",
                        Description = default
                    }
                }
            },
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        var genericScript = new CommandTemplateLocationDefinition()
        {
            PreparationCommand = default,
            ExecutionCommand = default,
            LocationType = ELocationType.HPC,
            CommandTemplateDefinition = new CommandTemplateDefinition
            {
                Name = "GenericCommandTemplate",
                Description = "Generic template script",
                Type = ECommandTemplateType.Generic,
                ComandTemplateParameterDefinitions = []
            },
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        return [
            jobScript,
            containerScript,
            bulkCopyScript,
            testTemplateScript,
            genericScript
        ];
    }
}