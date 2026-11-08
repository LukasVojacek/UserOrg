using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ProjectResourceTableConfiguration : IEntityTypeConfiguration<ProjectResource>
{
    private readonly string _tableName = "ProjectResource";
    public void Configure(EntityTypeBuilder<ProjectResource> builder)
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

        builder.Property(x => x.PrimaryInvestigatorEmail)
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

        // ADR-0026: a resource belongs to exactly one provider (nullable until assigned).
        builder.Property(x => x.ProviderId)
            .IsRequired(false);

        builder.HasOne<Provider>()
            .WithMany()
            .HasForeignKey(x => x.ProviderId)
            .HasConstraintName($"FK_{_tableName}_Provider_ProviderId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasIndex(x => x.Name)
            .IsUnique(false)
            .HasDatabaseName($"IX_{_tableName}_Name");

        // ADR-0026: a resource name is unique within a provider (active rows only).
        builder.HasIndex(x => new { x.ProviderId, x.Name })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ProviderId] IS NOT NULL")
            .HasDatabaseName($"IX_{_tableName}_ProviderId_Name");

    }
}