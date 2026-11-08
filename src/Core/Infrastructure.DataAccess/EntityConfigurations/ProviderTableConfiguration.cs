using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ProviderTableConfiguration : IEntityTypeConfiguration<Provider>
{
    private readonly string _tableName = "Provider";

    public void Configure(EntityTypeBuilder<Provider> builder)
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
            .HasMaxLength(DbConstants.ExtendedDescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ExtendedDescriptionMaxLength})")
            .IsUnicode(true);

        builder.Property(x => x.SupportEmail)
            .IsRequired(false)
            .HasMaxLength(DbConstants.EmailMaxLength)
            .HasColumnType($"nvarchar({DbConstants.EmailMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.IsPrivate)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        // Active-set uniqueness of the provider name (ADR-0027). Filtered to !IsDeleted so a
        // deactivated provider never blocks reusing its name; the active set is already unique
        // (additive, no dedup).
        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Provider_Name");

        builder.HasOne(x => x.Country)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Country_CountryId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}