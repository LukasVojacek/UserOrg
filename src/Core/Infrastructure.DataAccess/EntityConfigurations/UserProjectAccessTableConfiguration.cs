using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class UserProjectAccessTableConfiguration : IEntityTypeConfiguration<UserProjectAccess>
{
    private readonly string _tableName = "UserProjectAccess";
    public void Configure(EntityTypeBuilder<UserProjectAccess> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.HasAccess)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.ProjectId)
            .IsRequired()
            .HasConversion(
                value => value.Value,
                value => ProjectId.Create(value)
            );

        builder.Property(x => x.ProjectResourceId)
            .IsRequired(false)
            .HasDefaultValue(null);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");


        builder.HasOne(x => x.ProjectResource)
            .WithMany(x => x.AssignedUsers)
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_ProjectResource_ProjectResourceId")
            .HasForeignKey("ProjectResourceId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.AssignedByUser)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_AssignedByUserId")
            .HasForeignKey("AssignedByUserId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.UserId, x.ProjectId, x.ProjectResourceId })
           .IsUnique()
           .HasFilter("[ProjectResourceId] IS NOT NULL")
           .HasDatabaseName($"UX_{_tableName}_UserId_ProjectId_ProjectResourceId")
           .IncludeProperties("AssignedByUserId", "CreatedAt", "IsDeleted", "ModifiedAt");

        builder.HasIndex(x => x.ProjectId)
           .IsUnique(false)
           .HasDatabaseName($"IX_{_tableName}_ProjectId");

        builder.HasIndex(x => x.UserId)
           .IsUnique(false)
           .HasDatabaseName($"IX_{_tableName}_UserId");
    }
}