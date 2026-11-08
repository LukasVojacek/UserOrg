using Forge.Core.Features.Permission.Persistence;
using Forge.Core.Features.Role.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

namespace Forge.Core.Persistence.Configuration;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.RoleNameMaxLength);

        builder.HasIndex(r => r.Name)
            .IsUnique()
            .HasDatabaseName("IX_Roles_Name_Unique");

        builder.Property(r => r.Description)
            .HasMaxLength(DbConstants.ExtendedDescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ExtendedDescriptionMaxLength})");

        builder.Property(r => r.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true);

        builder.Property(r => r.Level)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(r => r.Category)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("Default");

        builder.Property<DateTimeOffset>("CreatedAt")
            .IsRequired()
            .HasColumnType("datetimeoffset(3)")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()")
            .ValueGeneratedOnAdd();

        // Many-to-many: Roles <-> Permissions via join table RolePermissions
        builder
            .HasMany(r => r.RolePermissions)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "RolePermissions",
                right => right.HasOne<Permission>()
                    .WithMany()
                    .HasForeignKey("PermissionId")
                    .OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne<Role>()
                    .WithMany()
                    .HasForeignKey("RoleId")
                    .OnDelete(DeleteBehavior.Restrict),
                je =>
                {
                    je.HasKey("RoleId", "PermissionId");
                    je.ToTable("RolePermissions");
                    je.HasIndex(["PermissionId", "RoleId"]).HasDatabaseName("IX_RolePermissions_PermissionId_RoleId");
                });
    }
}