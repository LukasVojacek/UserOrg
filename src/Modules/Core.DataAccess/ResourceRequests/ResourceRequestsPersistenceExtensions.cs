using Core.DataAccess.Queries.Contracts;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.Persistance;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Registers the dedicated, schema-isolated resource-request projection unit (ADR-0063 D7). Mirrors
/// <c>AddOutboxPersistence</c> / <c>AddHeappeIntegrationPersistence</c>: a pooled
/// <see cref="ResourceRequestsDbContext"/> factory against the same physical database (connection string
/// <c>UserOrgDBContext</c>) but its own <c>resourceRequests</c> schema and migrations history, a startup
/// migrator, and the read-query adapter.
/// </summary>
/// <remarks>
/// This entry point is intentionally NOT called anywhere here — the shared Core composition wiring is owned by
/// block B09, which invokes <see cref="AddResourceRequestsPersistence"/> from the host builder.
/// </remarks>
public static class ResourceRequestsPersistenceExtensions
{
    private const string ApplicationName = "UserOrg.ResourceRequests";
    private const int CommandTimeOutSeconds = 30;
    private const int RetryDelayInSeconds = 30;

    /// <summary>
    /// Adds the resource-request projection persistence unit to the host: a pooled
    /// <see cref="ResourceRequestsDbContext"/> factory wired to the <c>resourceRequests</c> schema with its own
    /// <c>__EFMigrationsHistory</c>, a hosted migration startup service, and the
    /// <see cref="IProjectResourceRequestQuery"/> read adapter.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddResourceRequestsPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("UserOrgDBContext");
        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName
        };

        builder.Services.AddPooledDbContextFactory<ResourceRequestsDbContext>(opt =>
        {
            opt.ConfigureSqlCommandLogging(builder.Environment.IsDevelopment());
            opt.UseSqlServer(connectionStringBuilder.ConnectionString, sqlOpt =>
            {
                sqlOpt.MigrationsAssembly(typeof(ResourceRequestsDbContext).Assembly);
                sqlOpt.MigrationsHistoryTable("__EFMigrationsHistory", ResourceRequestsDbContext.SchemaName);
                sqlOpt.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(RetryDelayInSeconds),
                    errorNumbersToAdd: null);
                sqlOpt.CommandTimeout(CommandTimeOutSeconds);
            });
        });

        builder.Services.AddHostedService<ResourceRequestsMigrationStartupService>();
        builder.Services.AddScoped<IProjectResourceRequestQuery, ProjectResourceRequestQuery>();

        return builder;
    }
}