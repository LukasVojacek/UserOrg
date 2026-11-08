using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<Domain.User.User>
{
    public void Configure(EntityTypeBuilder<Domain.User.User> builder)
    {
        // Shadow PK typed as UserId VO so FK relationships match CLR types
        // ValueGeneratedNever: ID is generated in adapter layer
        builder.Property<UserId>("Id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .ValueGeneratedNever();

        builder.HasKey("Id");

        // UserName and IdentityProviderId uniqueness is enforced by the unique indexes below, matching the
        // deployed schema. Email has no unique constraint in the database, so it is not modeled as a key.

        builder.Property(u => u.Email)
            .HasConversion(
                email => email.Value,
                value => Email.Create(value))
            .IsRequired()
            .HasMaxLength(Email.MaxLenght)
            .IsUnicode(false)
            .HasColumnType("nvarchar(100)");

        builder.Property(u => u.UserName)
            .HasConversion(
                userName => userName.Value,
                value => UserName.Create(value))
            .IsRequired()
            .HasMaxLength(UserName.MaxLenght)
            .IsUnicode(false)
            .HasColumnType("nvarchar(100)");

        builder.HasIndex(u => u.UserName)
            .IsUnique()
            .HasDatabaseName("UX_User_UserName");

        builder.Property<IdpId>(u => u.IdentityProviderId)
            .HasColumnName("IdentityProviderUserId")
            .IsRequired(true)
            .HasConversion<string>(
                id => id.Value,
                value => IdpId.Create(value))
            .HasMaxLength(IdpId.MaxLenght)
            .IsUnicode(false)
            .HasColumnType("nvarchar(150)");

        builder.HasIndex(u => u.IdentityProviderId)
            .IsUnique()
            .HasDatabaseName("UX_User_IdentityProviderUserId")
            .IncludeProperties("_assignedProviderId", "Id");




        // Cross-aggregate FK: single-column schema backed by _assignedProviderId field.
        // TODO: Replace with HasMany mapping when DB schema migrates to UserProvider junction table.
        builder.Property<ProviderId?>("_assignedProviderId")
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                value => value.HasValue ? new ProviderId(value.Value) : null)
            .HasColumnName("AssignedProviderId");

        builder.HasIndex("_assignedProviderId")
            .HasDatabaseName("IX_User_AssignedProviderId");

        // Cross-aggregate FK present in the deployed schema (optional; no inverse navigation here).
        builder.HasOne<Core.Domain.Provider.Provider>()
            .WithMany()
            .HasForeignKey("_assignedProviderId")
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_User_Provider_AssignedProviderId");

        builder.Property(u => u.IsDeleted)
            .HasDefaultValue(false);

        builder.Property<DateTimeOffset>("CreatedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false)
            .HasDefaultValueSql("null");

        // System permissions only – named query filter "SystemRoleOnly" in UserPermissionConfiguration
        // scopes this collection to ProjectId IS NULL. The inverse User navigation is shared with the
        // Project._memberRoles relationship (same UserId FK), so project-role rows can resolve their owner.
        // Deployed FK_UserPermission_User_UserId is ON DELETE NO ACTION (matches legacy snapshot).
        builder.HasMany<UserPermission>("_systemPermissions")
            .WithOne(up => up.User)
            .HasForeignKey("UserId")
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction);

        // UserPreference backing field – domain exposes computed JsonDocument via UserPreferences property.
        // Deployed FK_UserPreference_User_UserId is ON DELETE NO ACTION (matches legacy snapshot).
        builder.HasOne<UserPreference>("_userPreference")
            .WithOne()
            .HasForeignKey<UserPreference>(up => up.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasQueryFilter(u => !u.IsDeleted);

        builder.ToTable("User");


        builder.Ignore(u => u.HasAssignedProvider);
        builder.Ignore(u => u.AssignedProviderIds);
    }
}