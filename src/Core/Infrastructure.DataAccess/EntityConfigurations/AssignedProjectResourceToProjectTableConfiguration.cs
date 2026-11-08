using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class AssignedProjectResourceToProjectTableConfiguration : IEntityTypeConfiguration<AssignedProjectResourceToProject>
{
    private readonly string _tableName = "AssignedProjectResourceToProject";
    public void Configure(EntityTypeBuilder<AssignedProjectResourceToProject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property<Guid>("ProjectResourceId");
        builder.HasKey("ProjectId", "ProjectResourceId");

        builder.HasOne(x => x.ProjectResource)
            .WithMany(x => x.AssignedProjects)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_ProjectResource_ProjectResourceId")
            .HasForeignKey(x => x.ProjectResourceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Project)
            .WithMany(x => x.AssignedProjectResources)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Project_ProjectId")
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}