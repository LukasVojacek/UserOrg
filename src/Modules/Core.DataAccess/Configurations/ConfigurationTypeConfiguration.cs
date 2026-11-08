using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps ConfigurationType reference/lookup table to database. Matches the existing schema exactly.
/// </summary>
public sealed class ConfigurationTypeConfiguration : IEntityTypeConfiguration<ConfigurationType>
{
    public void Configure(EntityTypeBuilder<ConfigurationType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedOnAdd();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Description)
            .IsRequired(false)
            .HasMaxLength(150);

        builder.ToTable("ConfigurationType");
    }
}