using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using UserOrg.Shared.Persistance;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Registers the dedicated, schema-isolated outbox persistence unit (ADR-0041). Mirrors
/// <c>AddHeappeIntegrationPersistence</c>: a pooled <see cref="OutboxDbContext"/> factory against the same
/// physical database (connection string <c>UserOrgDBContext</c>) but its own <c>outbox</c> schema and
/// migrations history, plus a startup migrator.
/// </summary>
public static class OutboxPersistenceExtensions
{
    private const string ApplicationName = "UserOrg.Outbox";
    private const int CommandTimeOutSeconds = 30;
    private const int RetryDelayInSeconds = 30;

    /// <summary>
    /// Adds the outbox persistence unit to the host: a pooled <see cref="OutboxDbContext"/> factory wired to
    /// the <c>outbox</c> schema with its own <c>__EFMigrationsHistory</c>, and a hosted migration startup service.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddOutboxPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString = builder.Configuration.GetConnectionString("UserOrgDBContext");
        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName
        };

        builder.Services.AddPooledDbContextFactory<OutboxDbContext>(opt =>
        {
            opt.ConfigureSqlCommandLogging(builder.Environment.IsDevelopment());
            opt.UseSqlServer(connectionStringBuilder.ConnectionString, sqlOpt =>
            {
                sqlOpt.MigrationsAssembly(typeof(OutboxDbContext).Assembly);
                sqlOpt.MigrationsHistoryTable("__EFMigrationsHistory", OutboxDbContext.SchemaName);
                sqlOpt.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(RetryDelayInSeconds),
                    errorNumbersToAdd: null);
                sqlOpt.CommandTimeout(CommandTimeOutSeconds);
            });
        });

        builder.Services.AddHostedService<OutboxMigrationStartupService>();

        // Dispatcher (O4, ADR-0040): resolve stored event types, deliver via the swappable in-process
        // target (broker-ready seam), and run the poll loop as a hosted service.
        builder.Services.AddSingleton<IOutboxEventTypeResolver, OutboxEventTypeResolver>();
        builder.Services.AddSingleton<IOutboxDispatchTarget, InProcessOutboxDispatchTarget>();
        builder.Services.AddHostedService<OutboxDispatcherService>();

        return builder;
    }
}