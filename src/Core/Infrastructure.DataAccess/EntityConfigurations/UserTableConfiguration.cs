using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class UserTableConfiguration : IEntityTypeConfiguration<User>
{
    private readonly string _tableName = "User";
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.IdentityProviderUserId)
            .IsRequired()
            .HasMaxLength(DbConstants.IdentityProviderUserIdMaxLength)
            .HasColumnType($"nvarchar({DbConstants.IdentityProviderUserIdMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(DbConstants.EmailMaxLength)
            .HasColumnType($"nvarchar({DbConstants.EmailMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.UserName)
            .IsRequired()
            .HasMaxLength(DbConstants.UserNameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.UserNameMaxLength})")
            .IsUnicode(false);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.AssignedProvider)
            .WithMany(x => x.ResponsibleUsers)
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_Provider_AssignedProviderId");

        builder.HasIndex("IdentityProviderUserId")
            .IsUnique()
            .HasDatabaseName($"UX_{_tableName}_IdentityProviderUserId")
            .IncludeProperties("AssignedProviderId", "Id");

        builder.HasIndex(x => x.UserName)
            .IsUnique()
            .HasDatabaseName($"UX_{_tableName}_UserName");
    }
}