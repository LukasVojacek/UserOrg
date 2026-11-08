
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.Property<Guid>("Id");

        builder.HasKey("Id");

        builder.Property<DateTimeOffset>("CreatedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false)
            .HasDefaultValueSql("null");


        builder.Property(up => up.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired()
            .HasColumnName("UserId");

        builder.Property(up => up.ProjectId)
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                value => value.HasValue ? (ProjectId?)new ProjectId(value.Value) : null)
            .HasColumnName("ProjectId");

        builder.Property(up => up.SystemRoleId)
            .HasConversion(id => id.Value, value => new ProjectRoleId(value))
            .IsRequired()
            .HasColumnName("SystemRoleId");

        builder.Property(up => up.IsDeleted)
            .HasDefaultValue(false);

        // Scope User aggregate's navigation to system-wide roles only.
        // Named filter (EF Core 10) so the Project repository can bypass *only* this one via
        // IgnoreQueryFilters(["SystemRoleOnly"]) when hydrating Project._memberRoles, without also
        // disabling unrelated filters (e.g. Project soft-delete).
        builder.HasQueryFilter("SystemRoleOnly", up => up.ProjectId == null);

        builder.ToTable("UserPermission");
    }
}