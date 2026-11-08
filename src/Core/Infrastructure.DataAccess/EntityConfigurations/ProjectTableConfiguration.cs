using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ProjectTableConfiguration : IEntityTypeConfiguration<Project>
{
    private readonly string _tableName = "Project";
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Id)
            .IsRequired(true)
            .HasConversion(
                value => value.Value,
                value => ProjectId.Create(value)
            );

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.NameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.NameMaxLength})")
            .IsUnicode(true);

        builder.Property(x => x.Description)
            .IsRequired(false)
            .HasMaxLength(DbConstants.ExtendedDescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ExtendedDescriptionMaxLength})")
            .IsUnicode(true);

        builder.Property(x => x.ShortName)
            .IsRequired()
            .HasMaxLength(DbConstants.ProjectShortNameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ProjectShortNameMaxLength})")
            .HasConversion(v =>
                v.Value,
                v => ProjectShortName.Create(v)
             );

        builder.Property(x => x.Email)
            .IsRequired(false)
            .HasMaxLength(DbConstants.EmailMaxLength)
            .HasColumnType($"nvarchar({DbConstants.EmailMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.StartDate)
            .IsRequired()
            .HasColumnType($"datetime2({DbConstants.DateTimePrecision})");

        builder.Property(x => x.EndDate)
            .IsRequired()
            .HasColumnType($"datetime2({DbConstants.DateTimePrecision})");

        // Primary keys
        // TODO NEED to be changed in future
        builder.HasKey(x => x.Id)
            .HasName($"PK_ProjectConfiguration");

        builder.HasOne(x => x.ProjectDomainType)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_ProjectDomainType_ProjectDomainTypeId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_CreatedByUserId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.ShortName)
            .IsUnique()
            .HasDatabaseName($"UX_{_tableName}_ProjectShortName");
    }
}