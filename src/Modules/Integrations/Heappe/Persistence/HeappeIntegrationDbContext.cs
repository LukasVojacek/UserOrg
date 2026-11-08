using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Persistance;

namespace Heappe.Persistence;

internal class HeappeIntegrationDbContext : DbContext
{
    internal const string SchemaName = "heappe_integration";

    internal DbSet<CommandTemplateDefinition> CommandTemplateDefinitions { get; set; }
    internal DbSet<LocationPartition> LocationPartitions { get; set; }
    internal DbSet<ProjectCommandTemplate> ProjectCommandTemplates { get; set; }
    internal DbSet<LocationCommandTemplateKillSwitch> LocationCommandTemplateKillSwitches { get; set; }
    internal DbSet<LocationCommandTemplateWhitelist> LocationCommandTemplateWhitelists { get; set; }


    public HeappeIntegrationDbContext(DbContextOptions<HeappeIntegrationDbContext> options)
        : base(options)
    {

    }


    internal HeappeIntegrationDbContext(DbContextOptions<HeappeIntegrationDbContext> options, ILogger<HeappeIntegrationDbContext> logger)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.HasAnnotation("Relational:MigrationHistoryTableName", "__EFMigrationsHistory");
        modelBuilder.HasAnnotation("Relational:MigrationHistoryTableSchema", SchemaName);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HeappeIntegrationDbContext).Assembly);

        modelBuilder.ApplyEnumEntityConfiguration();
        modelBuilder.ApplyAuditableEntityConfiguration();
        modelBuilder.ApplySoftDeleteQueryFilters();

    }

    /// <summary>
    /// Applies a global query filter to exclude soft-deleted entities from queries for all entity types implementing
    /// the IAuditableEntity interface.
    /// </summary>
    /// <remarks>This method iterates through all entity types in the model and applies a query filter that
    /// checks the IsDeleted property, ensuring that only non-deleted entities are included in the results.</remarks>
    /// <param name="modelBuilder">The ModelBuilder used to configure the model and apply the query filters.</param>

}