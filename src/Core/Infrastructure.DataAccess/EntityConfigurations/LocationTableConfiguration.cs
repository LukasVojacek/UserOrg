using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class LocationTableConfiguration : IEntityTypeConfiguration<Location>
{
    private readonly string _tableName = "Location";

    public void Configure(EntityTypeBuilder<Location> builder)
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

        builder.Property(x => x.Endpoint)
            .IsRequired()
            .HasMaxLength(DbConstants.EndpointNameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.EndpointNameMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.IsPrivate)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.IsShared)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.DefaultServiceIntegrationUrl)
            .IsRequired(false)
            .HasMaxLength(DbConstants.EndpointNameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.EndpointNameMaxLength})")
            .HasDefaultValueSql("NULL")
            .IsUnicode(false)
            .HasConversion(
                uri => uri != null
                    ? uri.ToString()
                    : default,
                str => !string.IsNullOrWhiteSpace(str)
                    ? new Uri(str)
                    : default
             );

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.Property(x => x.ProviderId);
        builder.Property(x => x.LocationTypeId);

        builder.HasOne(x => x.Provider)
            .WithMany(x => x.Locations)
            .IsRequired()
            .HasForeignKey(x => x.ProviderId)
            .HasConstraintName($"FK_{_tableName}_Provider_ProviderId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.LocationType)
            .WithMany()
            .IsRequired()
            .HasForeignKey(x => x.LocationTypeId)
            .HasConstraintName($"FK_{_tableName}_LocationType_LocationTypeId")
            .OnDelete(DeleteBehavior.NoAction);

    }
}