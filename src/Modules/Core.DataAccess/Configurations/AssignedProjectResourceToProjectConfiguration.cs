
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps AssignedProjectResourceToProject junction entity to database.
/// Composite PK: (ProjectId, ProjectResourceId) — matches actual DB schema.
/// </summary>
public class AssignedProjectResourceToProjectConfiguration : IEntityTypeConfiguration<AssignedProjectResourceToProject>
{
    internal const string ActiveFilter = "AssignedResourceActive";

    public void Configure(EntityTypeBuilder<AssignedProjectResourceToProject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<ProjectId>("ProjectId")
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .IsRequired();

        builder.Property<ProjectResourceId>("ProjectResourceId")
            .HasConversion(id => id.Value, value => ProjectResourceId.From(value))
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        // Composite PK matches actual DB schema — no separate Id column exists
        builder.HasKey("ProjectId", "ProjectResourceId");

        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        // Audit timestamp kept off the pure domain entity — mapped as an EF shadow property so the
        // UnitOfWork's DateTimeOffset stamp on Modified entries fits the CLR type.
        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false);

        builder.Property(a => a.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne<ProjectResource>("ProjectResource")
            .WithMany()
            .HasForeignKey("ProjectResourceId")
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex("ProjectId")
            .HasDatabaseName("IX_AssignedProjectResourceToProject_ProjectId");

        builder.HasIndex("ProjectResourceId")
            .HasDatabaseName("IX_AssignedProjectResourceToProject_ProjectResourceId");

        // Named filter so the project repository can ignore only this soft-delete filter and load the
        // deleted links (needed to reactivate one on re-assign instead of inserting a duplicate key).
        // The type is therefore skipped by the soft-delete convention in DatabaseContext.OnModelCreating.
        builder.HasQueryFilter(ActiveFilter, a => !a.IsDeleted);

        builder.ToTable("AssignedProjectResourceToProject");
    }
}