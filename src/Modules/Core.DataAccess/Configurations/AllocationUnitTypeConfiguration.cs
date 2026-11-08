using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps AllocationUnitType reference/lookup table to database. Matches the existing schema exactly.
/// </summary>
public sealed class AllocationUnitTypeConfiguration : IEntityTypeConfiguration<AllocationUnitType>
{
    public void Configure(EntityTypeBuilder<AllocationUnitType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedOnAdd();

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Description)
            .IsRequired(false)
            .HasMaxLength(150);

        builder.ToTable("AllocationUnitType");
    }
}