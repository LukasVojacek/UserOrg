using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

namespace Heappe.Persistence.Configurations;

internal sealed class ProjectCommandTemplateConfiguration : IEntityTypeConfiguration<ProjectCommandTemplate>
{
    private readonly string _tableName = "ProjectCommandTemplate";
    public void Configure(EntityTypeBuilder<ProjectCommandTemplate> builder)
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
            .HasMaxLength(DbConstants.CommandTemplateNameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.CommandTemplateNameMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasConversion(
                v => (int)v,
                v => (ECommandTemplateType)v);

        builder.Property(x => x.ProjectId)
            .IsRequired();

        builder.Property(x => x.ProjectResourceId)
            .IsRequired();

        builder.Ignore(x => x.State);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.CommandTemplateDefinition)
            .WithMany(x => x.ProjectCommandTemplates)
            .HasForeignKey(x => x.CommandTemplateDefinitionId)
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_CommandTemplateDefinition_CommandTemplateDefinitionId")
            .OnDelete(DeleteBehavior.NoAction);


        builder.HasIndex(x => new { x.ProjectId, x.ProjectResourceId, x.Name })
            .IsUnique()
            .HasDatabaseName($"UX_{_tableName}_ProjectId_ProjectResourceId_Name");

        builder.HasIndex(x => x.ProjectResourceId)
            .HasDatabaseName($"IX_{_tableName}_ProjectResourceId");

        builder.HasIndex(x => x.Type)
            .HasDatabaseName($"IX_{_tableName}_Type");
    }
}