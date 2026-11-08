using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;


namespace Heappe.Persistence.Configurations;

internal sealed class CommandTemplateDefinitionConfiguration : IEntityTypeConfiguration<CommandTemplateDefinition>
{
    private readonly string _tableName = "CommandTemplateDefinition";

    public void Configure(EntityTypeBuilder<CommandTemplateDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        // ponytail: client-generated Guid v7 PK — do not restore a DB default, see #280 and
        // docs/architecture/adr/0061-legacy-write-paths-client-generate-guid-pks.md (2026-08-12
        // amendment — this context ctor-mints + ValueGeneratedNever, not the shared convention).
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.NameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.NameMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.Description)
            .HasMaxLength(DbConstants.DescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.DescriptionMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasConversion(
                v => (int)v,
                v => (ECommandTemplateType)v
             );

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasIndex(x => new { x.Name })
            .IsUnique()
            .HasDatabaseName($"UX_{_tableName}_Name");
    }
}