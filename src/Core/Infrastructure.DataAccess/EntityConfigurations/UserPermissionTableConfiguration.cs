using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class UserPermissionTableConfiguration : IEntityTypeConfiguration<UserPermission>
{
    private readonly string _tableName = "UserPermission";
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.HasKey(x => x.Id)
           .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.Project)
            .WithMany(p => p.UserPermissions)
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_Project_ProjectId")
            .OnDelete(DeleteBehavior.NoAction);


        builder.HasOne(x => x.User)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_UserId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}