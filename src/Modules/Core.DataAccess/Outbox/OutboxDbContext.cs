using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Dedicated, schema-isolated persistence unit for the transactional outbox (ADR-0041), modelled on the
/// HEAppE integration context. Maps a single <see cref="OutboxMessage"/> to <c>outbox.Outbox</c> with its
/// own default-named <c>__EFMigrationsHistory</c> in the <c>outbox</c> schema. Never touches <c>dbo</c>,
/// the modular <c>DatabaseContext</c>, or the drifted modular snapshot.
/// </summary>
internal sealed class OutboxDbContext : DbContext
{
    internal const string SchemaName = "outbox";

    public OutboxDbContext(DbContextOptions<OutboxDbContext> options)
        : base(options)
    {
    }

    internal DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.HasAnnotation("Relational:MigrationHistoryTableName", "__EFMigrationsHistory");
        modelBuilder.HasAnnotation("Relational:MigrationHistoryTableSchema", SchemaName);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}