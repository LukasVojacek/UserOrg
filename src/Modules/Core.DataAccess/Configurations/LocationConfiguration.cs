using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the Location entity (Provider aggregate) to the Location table. Reproduces the existing schema.
/// Persistence-only columns (ServiceName, DefaultServiceIntegrationUrl, ProviderId, audit) are shadow
/// properties; Mode and LocationType are mapped domain properties over their existing columns.
/// </summary>
public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.Property<Guid>("Id")
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_Location");

        builder.Property(l => l.Name)
            .HasConversion(name => name.Value, value => LocationName.Create(value))
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)");

        builder.Property(l => l.Description)
            .HasMaxLength(150)
            .HasColumnType("nvarchar(150)");

        builder.Property(l => l.Endpoint)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)");

        builder.Property(l => l.IsPrivate)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        builder.Property(l => l.IsShared)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(true);

        builder.Property(l => l.IsDeleted)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        // ValueGeneratedOnAdd: inserts keep the DB default; the domain reads Mode for its guard only.
        builder.Property(l => l.Mode)
            .HasConversion<int>()
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(Core.Domain.Shared.EOperationMode.Unspecified);

        builder.Property<string>("ServiceName")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)")
            .HasDefaultValueSql("'LEXIS'");

        builder.Property<string>("DefaultServiceIntegrationUrl")
            .ValueGeneratedOnAdd()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)")
            .HasDefaultValueSql("NULL");

        builder.Property(l => l.LocationType)
            .HasConversion<int>()
            .HasColumnName("LocationTypeId")
            .HasColumnType("int");

        builder.Property<ProviderId>("ProviderId")
            .HasConversion(id => id.Value, value => ProviderId.From(value))
            .HasColumnType("uniqueidentifier");

        builder.Property<DateTime>("CreatedAt")
            .ValueGeneratedOnAdd()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("null");

        builder.HasIndex(l => l.LocationType)
            .HasDatabaseName("IX_Location_LocationTypeId");

        builder.HasOne<LocationType>()
            .WithMany()
            .HasForeignKey(l => l.LocationType)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_Location_LocationType_LocationTypeId");

        // why: ClientCascade per ADR-0115. The database FK stays NO ACTION.
        builder.HasMany<AssignedLocation>("_locationAllocations")
            .WithOne()
            .HasForeignKey("LocationId")
            .OnDelete(DeleteBehavior.ClientCascade)
            .IsRequired()
            .HasConstraintName("FK_AssignedLocation_Location_LocationId");

        // Read-only projection over the backing field — not a separate EF navigation.
        builder.Ignore(l => l.AssignedLocations);

        builder.HasQueryFilter(l => !l.IsDeleted);

        builder.ToTable("Location");
    }
}