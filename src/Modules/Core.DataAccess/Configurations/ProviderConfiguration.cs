using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the Provider aggregate to the Provider table. Reproduces the existing schema exactly —
/// audit fields, Id and lookup FKs are shadow/value-converted so no migration is generated.
/// </summary>
public sealed class ProviderConfiguration : IEntityTypeConfiguration<Domain.Provider.Provider>
{
    public void Configure(EntityTypeBuilder<Domain.Provider.Provider> builder)
    {
        builder.Property<ProviderId>("Id")
            .HasConversion(id => id.Value, value => ProviderId.From(value))
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_Provider");

        builder.Property(p => p.Name)
            .HasConversion(name => name.Value, value => ProviderName.From(value))
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)");

        builder.Property(p => p.Description)
            .HasConversion<DescriptionNullableConverter>()
            .HasMaxLength(500)
            .HasColumnType("nvarchar(500)");

        builder.Property(p => p.SupportEmail)
            .HasConversion(
                email => email.HasValue ? email.Value.Value : null,
                value => string.IsNullOrEmpty(value) ? (Email?)null : new Email(value))
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)");

        builder.Property(p => p.ExternalId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : null,
                value => string.IsNullOrEmpty(value) ? (ExternalId?)null : ExternalId.Create(value))
            .ValueGeneratedOnAdd()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)")
            .HasDefaultValueSql("null");

        builder.Property(p => p.Mode)
            .HasConversion(mode => (int)mode, value => (EOperationMode)value)
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(EOperationMode.Unspecified);

        builder.Property(p => p.ServiceName)
            .HasConversion(sn => sn.Value, value => ServiceName.Create(value))
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)")
            .HasDefaultValueSql("'LEXIS'");

        builder.Property(p => p.IsPrivate)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        builder.Property(p => p.CountryId)
            .HasColumnType("int");

        builder.Property(p => p.IsDeleted)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        builder.Property<DateTime>("CreatedAt")
            .ValueGeneratedOnAdd()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("null");

        builder.HasIndex(p => p.CountryId);

        // Active-set uniqueness of the provider name (ADR-0027). Mirrors the legacy filtered unique
        // IX_Provider_Name (migration AddProviderNameUniqueIndex). Filtered to !IsDeleted so a
        // deactivated provider never blocks reusing its name.
        builder.HasIndex(p => p.Name)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Provider_Name");

        builder.HasOne<Domain.Provider.Country>()
            .WithMany()
            .HasForeignKey(p => p.CountryId)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_Provider_Country_CountryId");

        // why: ClientCascade per ADR-0115. The database FK stays NO ACTION.
        builder.HasMany<Domain.Provider.Location>("_providerLocations")
            .WithOne()
            .HasForeignKey("ProviderId")
            .OnDelete(DeleteBehavior.ClientCascade)
            .IsRequired()
            .HasConstraintName("FK_Location_Provider_ProviderId");

        // Read-only projection over the backing field — not a separate EF navigation.
        builder.Ignore(p => p.Locations);

        // Named filter so FindIncludingDeactivatedAsync can ignore only the provider's own soft-delete
        // filter (to surface a deactivated provider) without also resurrecting soft-deleted child
        // Locations/AssignedLocations, whose anonymous filters stay active. Provider is therefore skipped
        // by the soft-delete convention in DatabaseContext.OnModelCreating.
        builder.HasQueryFilter("ProviderActive", p => !p.IsDeleted);

        builder.ToTable("Provider");
    }
}