using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;


namespace Heappe.Persistence.Configurations;

internal sealed class CommandTemplateParameterDefinitionConfiguration : IEntityTypeConfiguration<CommandTemplateParameterDefinition>
{
    private readonly string _tableName = "CommandTemplateParameterDefinition";
    public void Configure(EntityTypeBuilder<CommandTemplateParameterDefinition> builder)
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

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.CommandTemplateDefinition)
            .WithMany(x => x.ComandTemplateParameterDefinitions)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_CommandTemplateDefinition_CommandTemplateDefinitionId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}