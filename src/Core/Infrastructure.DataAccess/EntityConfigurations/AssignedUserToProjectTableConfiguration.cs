using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class AssignedUserToProjectTableConfiguration : IEntityTypeConfiguration<AssignedUserToProject>
{
    private readonly string _tableName = "AssignedUserToProject";
    public void Configure(EntityTypeBuilder<AssignedUserToProject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property<Guid>(x => x.ProjectId);
        builder.Property<Guid>("UserId");

        builder.HasKey("ProjectId", "UserId")
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.Project)
            .WithMany(x => x.AssignedUsers)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Project_ProjectId")
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.User)
            .WithMany(x => x.AssignedToProjects)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_UserId")
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property<Guid>("AssignedByUserId");
        builder.HasOne(x => x.AssignedByUser)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_AssignedByUserId")
            .HasForeignKey("AssignedByUserId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex("UserId", "ProjectId")
           .IsUnique(false)
           .HasDatabaseName($"IX_{_tableName}_UserId_ProjectId")
           .IncludeProperties("AssignedByUserId", "CreatedAt", "IsDeleted", "ModifiedAt");
    }
}