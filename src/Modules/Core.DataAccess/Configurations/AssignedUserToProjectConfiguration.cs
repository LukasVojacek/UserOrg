using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Configures AssignedUserToProject join entity with proper DDD boundaries:
/// - Shadow properties for all IDs (UserId, ProjectId, AssignedByUserId)
/// - Internal relationships back to User and Project (for EF only, not domain)
/// - Composite key: ProjectId + UserId (representing the assignment)
/// </summary>
public class AssignedUserToProjectConfiguration : IEntityTypeConfiguration<AssignedUserToProject>
{
    internal const string ActiveFilter = "AssignedUserActive";

    public void Configure(EntityTypeBuilder<AssignedUserToProject> builder)
    {
        // Entity VO properties mapped to primitive DB columns via value converters
        // DDD ideal: UserId should store UserName (natural key of User aggregate).
        // Requires DB migration: change UNIQUEIDENTIFIER → NVARCHAR, backfill from User.UserName, retarget FK.
        builder.Property(ap => ap.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired()
            .HasColumnName("UserId");

        // DDD ideal: ProjectId should store ProjectShortName (natural key of Project aggregate).
        // Requires DB migration: change UNIQUEIDENTIFIER → NVARCHAR, backfill from Project.ShortName, retarget FK.
        builder.Property(ap => ap.ProjectId)
            .HasConversion(id => id.Value, value => new ProjectId(value))
            .IsRequired()
            .HasColumnName("ProjectId");

        // DDD ideal: AssignedByUserId should store UserName (natural key of User aggregate).
        // Requires DB migration: change UNIQUEIDENTIFIER → NVARCHAR, backfill from User.UserName, retarget FK.
        builder.Property(ap => ap.AssignedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired()
            .HasColumnName("AssignedByUserId");

        // Audit timestamp kept off the pure domain entity — mapped as an EF shadow property.
        builder.Property<DateTimeOffset>("CreatedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false)
            .HasDefaultValueSql("null");

        builder.HasKey(ap => new { ap.ProjectId, ap.UserId })
            .HasName("PK_AssignedUserToProject");

        // Indexes match the deployed schema: one composite (UserId,ProjectId) with INCLUDE columns,
        // plus AssignedByUserId. (No ProjectId_UserId index exists in the legacy schema.)
        builder.HasIndex("UserId", "ProjectId")
            .HasDatabaseName("IX_AssignedUserToProject_UserId_ProjectId")
            .IncludeProperties("AssignedByUserId", "CreatedAt", "IsDeleted", "ModifiedAt");

        builder.HasIndex("AssignedByUserId")
            .HasDatabaseName("IX_AssignedUserToProject_AssignedByUserId");

        // Relationships - Internal to EF, NOT exposed to domain code
        // User reference - the user who is assigned to the project
        // Navigation named "User" so it can be included with path "._assignedUsers.User" at query time
        // All three FKs are ON DELETE NO ACTION in the deployed schema (match legacy snapshot).
        builder.HasOne<Domain.User.User>("User")
            .WithMany()
            .HasForeignKey(ap => ap.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_AssignedUserToProject_User_UserId");

        // AssignedByUser reference - the user who created the assignment
        // HasForeignKey maps AssignedByUserId (who assigned it)
        builder.HasOne<Domain.User.User>("AssignedByUser")
            .WithMany()
            .HasForeignKey(ap => ap.AssignedByUserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_AssignedUserToProject_User_AssignedByUserId");

        // Named filter so the project repository can ignore only this soft-delete filter and load the
        // deleted links (needed to reactivate one on re-assign instead of inserting a duplicate key).
        // The type is therefore skipped by the soft-delete convention in DatabaseContext.OnModelCreating.
        builder.HasQueryFilter(ActiveFilter, ap => !ap.IsDeleted);

        builder.ToTable("AssignedUserToProject");
    }
}