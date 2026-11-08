using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Design-time factory used by the EF Core tooling (<c>dotnet ef migrations add</c>). Mirrors the HEAppE
/// integration factory: builds options with a placeholder/env connection string and points the migrations
/// history at the default-named table in the <c>outbox</c> schema. <c>migrations add</c> only diffs the
/// model against the snapshot and never opens a connection.
/// </summary>
internal sealed class OutboxDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OutboxDbContext>
{
    public OutboxDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings_OutboxDbContext")
            ?? "Server=localhost;Database=DesignTime;Trusted_Connection=True;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(OutboxDbContext).Assembly);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", OutboxDbContext.SchemaName);
            });

        return new OutboxDbContext(optionsBuilder.Options);
    }
}