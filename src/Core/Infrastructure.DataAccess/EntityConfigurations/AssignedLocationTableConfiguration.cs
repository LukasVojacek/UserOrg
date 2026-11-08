using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class AssignedLocationTableConfiguration : IEntityTypeConfiguration<AssignedLocation>
{
    private readonly string _tableName = "AssignedLocation";
    public void Configure(EntityTypeBuilder<AssignedLocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.AllocationUnitCount)
            .IsRequired(false);

        builder.Property(x => x.Description)
            .IsRequired(false)
            .HasMaxLength(DbConstants.DescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.DescriptionMaxLength})")
            .IsUnicode(true);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.Property(x => x.LocationId);
        builder.Property(x => x.ProjectResourceId);
        builder.Property(x => x.AllocationUnitTypeId);

        builder.HasOne(x => x.ProjectResource)
                .WithMany(x => x.AssignedLocations)
                .IsRequired()
                .HasForeignKey(x => x.ProjectResourceId)
                .HasConstraintName($"FK_{_tableName}_ProjectResource_ProjectResourceId")
                .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Location)
                .WithMany(x => x.AssignedLocations)
                .IsRequired()
                .HasForeignKey(x => x.LocationId)
                .HasConstraintName($"FK_{_tableName}_Location_LocationId")
                .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.AllocationUnitType)
                .WithMany()
                .IsRequired()
                .HasForeignKey(x => x.AllocationUnitTypeId)
                .HasConstraintName($"FK_{_tableName}_AllocationUnitType_AllocationUnitTypeId")
                .OnDelete(DeleteBehavior.NoAction);

        // ADR-0024: GUID-free public addressing slug (nullable; v1 rows stay null).
        builder.Property(x => x.Slug)
            .HasColumnType("nvarchar(120)");

        builder.HasIndex("LocationId", "ProjectResourceId")
            .IsUnique(false)
            .HasDatabaseName($"IX_{_tableName}_LocationId_ProjectResourceId");

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName($"IX_{_tableName}_Slug");
    }
}