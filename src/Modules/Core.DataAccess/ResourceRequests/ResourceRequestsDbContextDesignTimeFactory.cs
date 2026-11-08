using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Design-time factory used by the EF Core tooling (<c>dotnet ef migrations add</c>). Mirrors the Outbox /
/// HEAppE factories: builds options with a placeholder/env connection string and points the migrations
/// history at the default-named table in the <c>resourceRequests</c> schema. <c>migrations add</c> only diffs
/// the model against the snapshot and never opens a connection.
/// </summary>
internal sealed class ResourceRequestsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ResourceRequestsDbContext>
{
    public ResourceRequestsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings_ResourceRequestsDbContext")
            ?? "Server=localhost;Database=DesignTime;Trusted_Connection=True;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<ResourceRequestsDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ResourceRequestsDbContext).Assembly);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", ResourceRequestsDbContext.SchemaName);
            });

        return new ResourceRequestsDbContext(optionsBuilder.Options);
    }
}