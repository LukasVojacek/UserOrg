
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps ProjectDomainType reference/lookup table to database.
/// </summary>
public class ProjectDomainTypeConfiguration : IEntityTypeConfiguration<ProjectDomainType>
{
    public void Configure(EntityTypeBuilder<ProjectDomainType> builder)
    {
        // Id is internal; must use the matching CLR type ProjectDomainTypeId with a converter
        builder.Property<ProjectDomainTypeId>("Id")
            .HasConversion(id => id.Value, value => new ProjectDomainTypeId(value))
            .ValueGeneratedOnAdd();

        builder.HasKey("Id");

        builder.Property(p => p.Name)
            .HasConversion(n => n.Value, value => DomainTypeName.Create(value))
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(true)
            .HasColumnType("nvarchar(100)");

        builder.Property(p => p.Description)
            .IsRequired(false)
            .HasConversion<DescriptionNullableConverter>()
            .HasMaxLength(150)
            .IsUnicode(true)
            .HasColumnType("nvarchar(150)");

        builder.HasIndex(p => p.Name)
            .IsUnique();

        builder.ToTable("ProjectDomainType");
    }
}