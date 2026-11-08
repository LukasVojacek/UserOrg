using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps LocationType reference/lookup table to database. Matches the existing schema exactly.
/// </summary>
public sealed class LocationTypeConfiguration : IEntityTypeConfiguration<LocationType>
{
    public void Configure(EntityTypeBuilder<LocationType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasConversion<int>()
            .ValueGeneratedOnAdd();

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.Description)
            .IsRequired(false)
            .HasMaxLength(150);

        builder.ToTable("LocationType");
    }
}