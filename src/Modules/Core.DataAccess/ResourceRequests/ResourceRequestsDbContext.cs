using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Dedicated, schema-isolated persistence unit for the resource-assignment request projection (ADR-0063 D7),
/// modelled on the HEAppE integration and Outbox contexts. Maps a single <see cref="ProjectResourceRequest"/>
/// to <c>resourceRequests.ResourceRequests</c> with its own default-named <c>__EFMigrationsHistory</c> in the
/// <c>resourceRequests</c> schema. Never touches <c>dbo</c>, the modular <c>DatabaseContext</c>, or the frozen
/// modular snapshot.
/// </summary>
internal sealed class ResourceRequestsDbContext : DbContext
{
    internal const string SchemaName = "resourceRequests";

    public ResourceRequestsDbContext(DbContextOptions<ResourceRequestsDbContext> options)
        : base(options)
    {
    }

    internal DbSet<ProjectResourceRequest> ProjectRequests { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.HasAnnotation("Relational:MigrationHistoryTableName", "__EFMigrationsHistory");
        modelBuilder.HasAnnotation("Relational:MigrationHistoryTableSchema", SchemaName);

        modelBuilder.ApplyConfiguration(new ProjectResourceRequestConfiguration());
    }
}