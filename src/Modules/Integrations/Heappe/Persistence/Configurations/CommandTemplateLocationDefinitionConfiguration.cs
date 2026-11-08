using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;
using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Heappe.Persistence.Configurations;

internal sealed class CommandTemplateLocationDefinitionConfiguration : IEntityTypeConfiguration<CommandTemplateLocationDefinition>
{
    private readonly string _tableName = "CommandTemplateLocationDefinition";

    public void Configure(EntityTypeBuilder<CommandTemplateLocationDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        // ponytail: client-generated Guid v7 PK — do not restore a DB default, see #280 and
        // docs/architecture/adr/0061-legacy-write-paths-client-generate-guid-pks.md (2026-08-12
        // amendment — this context ctor-mints + ValueGeneratedNever, not the shared convention).
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.PreparationCommand)
            .HasMaxLength(DbConstants.ScriptPathMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ScriptPathMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.ExecutionCommand)
            .HasMaxLength(DbConstants.ScriptPathMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ScriptPathMaxLength})")
            .IsUnicode(false);


        builder.Property(x => x.LocationType)
            .IsRequired()
            .HasConversion(
                v => (int)v,
                v => (ELocationType)v
            );

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.CommandTemplateDefinition)
            .WithMany(x => x.CommandTemplateLocationDefinitions)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_CommandTemplateDefinition_CommandTemplateDefinitionId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.LocationType, x.CommandTemplateDefinitionId, x.LocationPartitionId })
            .IsUnique()
            .HasFilter("[LocationPartitionId] IS NOT NULL")
            .HasDatabaseName($"UX_{_tableName}_LocationType_CommandTemplateDefinitionId_LocationPartitionId");

        builder.HasIndex(x => new { x.LocationType, x.CommandTemplateDefinitionId })
            .IsUnique()
            .HasFilter("[LocationPartitionId] IS NULL")
            .HasDatabaseName($"UX_{_tableName}_LocationType_CommandTemplateDefinitionId_NullLocationPartitionId");
    }
}