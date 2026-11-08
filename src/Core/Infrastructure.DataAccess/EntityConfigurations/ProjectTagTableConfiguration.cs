using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ProjectTagTableConfiguration : IEntityTypeConfiguration<ProjectTag>
{
    private readonly string _tableName = "ProjectTag";
    public void Configure(EntityTypeBuilder<ProjectTag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.NameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.NameMaxLength})")
            .IsUnicode(true);

        builder.Property(x => x.Description)
            .IsRequired(false)
            .HasMaxLength(DbConstants.DescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.DescriptionMaxLength})")
            .IsUnicode(true);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasMany(x => x.Projects)
            .WithMany(x => x.ProjectTags)
            .UsingEntity(j => j.ToTable("AssignedProjectTagToProject"));

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .IsRequired()
            .HasForeignKey("CreatedByUserId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}