using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Heappe.Persistence.Configurations;

internal sealed class ProjectCommandTemplatePartitionConfiguration : IEntityTypeConfiguration<ProjectCommandTemplatePartition>
{
    private readonly string _tableName = "ProjectCommandTemplatePartition";
    public void Configure(EntityTypeBuilder<ProjectCommandTemplatePartition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        // ponytail: client-generated Guid v7 PK — do not restore a DB default, see #280 and
        // docs/architecture/adr/0061-legacy-write-paths-client-generate-guid-pks.md (2026-08-12
        // amendment — this context ctor-mints + ValueGeneratedNever, not the shared convention).
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CommandTemplateId)
            .IsRequired(false)
            .HasDefaultValueSql("null");

        builder.Property(x => x.State)
            .IsRequired()
            .HasConversion(
                v => (int)v,
                v => (ECommandTemplateState)v);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.LocationPartition)
            .WithMany(x => x.ProjectCommandTemplatePartitions)
            .HasForeignKey(x => x.LocationPartitionId)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_LocationPartition_LocationPartitionId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ProjectCommandTemplate)
            .WithMany(x => x.ProjectCommandTemplatePartitions)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_ProjectCommandTemplate_ProjectCommandTemplateId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.CommandTemplateId, x.State })
            .HasDatabaseName($"IX_{_tableName}_CommandTemplateId_State");
    }
}