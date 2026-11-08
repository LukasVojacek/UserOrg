using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the AssignedLocationValue child entity to the AssignedLocationValue table.
/// The table has no audit columns; Id and the AssignedLocationId FK are shadow properties.
/// </summary>
public sealed class AssignedLocationValueConfiguration : IEntityTypeConfiguration<AssignedLocationValue>
{
    public void Configure(EntityTypeBuilder<AssignedLocationValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<Guid>("Id")
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_AssignedLocationValue");

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)");

        builder.Property(a => a.Value)
            .IsRequired()
            .HasMaxLength(1500)
            .HasColumnType("nvarchar(1500)");

        builder.ToTable("AssignedLocationValue");
    }
}