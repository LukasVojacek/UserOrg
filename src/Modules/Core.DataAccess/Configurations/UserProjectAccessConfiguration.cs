
using Core.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps UserProjectAccess entity to database.
/// All IDs (including implicit PK) are shadow properties.
/// Direct mapping of user access to projects.
/// Column types/indexes/FKs mirror the legacy dbo schema exactly (ADR-0049 zero-diff overlay).
/// </summary>
public class UserProjectAccessConfiguration : IEntityTypeConfiguration<UserProjectAccess>
{
    public void Configure(EntityTypeBuilder<UserProjectAccess> builder)
    {
        // Shadow PK (NewId() default applied by AuditableEntityConventions).
        builder.Property<Guid>("Id")
            .ValueGeneratedOnAdd();

        builder.HasKey("Id");

        builder.Property<Guid>("UserId")
            .IsRequired();

        builder.Property<Guid>("ProjectId")
            .IsRequired();

        // Typed as the principals' VO keys so the cross-aggregate FKs below are type-compatible.
        builder.Property<ProjectResourceId?>("ProjectResourceId")
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                value => value.HasValue ? new ProjectResourceId(value.Value) : (ProjectResourceId?)null)
            .IsRequired(false);

        // AssignedByUserId is NON-nullable in the deployed schema.
        builder.Property<UserId>("AssignedByUserId")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        // Persistence-only operation mode column (no domain property).
        builder.Property<int>("Mode")
            .HasColumnType("int")
            .HasDefaultValue(0);

        builder.Property(u => u.HasAccess)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.ServiceName)
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(false)
            .HasColumnType("nvarchar(100)")
            .HasDefaultValueSql("'LEXIS'");

        // Audit timestamp kept off the pure domain entity — mapped as an EF shadow property so the
        // UnitOfWork's DateTimeOffset stamp on Modified entries fits the CLR type.
        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false);

        // CreatedAt / IsDeleted column shape applied by AuditableEntityConventions.

        builder.HasIndex("UserId")
            .HasDatabaseName("IX_UserProjectAccess_UserId");

        builder.HasIndex("ProjectId")
            .HasDatabaseName("IX_UserProjectAccess_ProjectId");

        builder.HasIndex("ProjectResourceId")
            .HasDatabaseName("IX_UserProjectAccess_ProjectResourceId");

        builder.HasIndex("AssignedByUserId")
            .HasDatabaseName("IX_UserProjectAccess_AssignedByUserId");

        builder.HasIndex("UserId", "ProjectId", "ProjectResourceId")
            .IsUnique()
            .HasFilter("[ProjectResourceId] IS NOT NULL")
            .HasDatabaseName("UX_UserProjectAccess_UserId_ProjectId_ProjectResourceId")
            .IncludeProperties("AssignedByUserId", "CreatedAt", "IsDeleted", "ModifiedAt");

        // Cross-aggregate FKs present in the deployed schema (both ON DELETE NO ACTION; no navigations).
        builder.HasOne<Core.Domain.User.User>()
            .WithMany()
            .HasForeignKey("AssignedByUserId")
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_UserProjectAccess_User_AssignedByUserId");

        builder.HasOne<Core.Domain.Provider.ProjectResource>()
            .WithMany()
            .HasForeignKey("ProjectResourceId")
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_UserProjectAccess_ProjectResource_ProjectResourceId");

        builder.ToTable("UserProjectAccess");
    }
}