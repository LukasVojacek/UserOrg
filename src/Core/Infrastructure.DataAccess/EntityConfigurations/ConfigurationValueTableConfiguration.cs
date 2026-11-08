using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ConfigurationValueTableConfiguration : IEntityTypeConfiguration<ConfigurationValue>
{
    private readonly string _tableName = "ConfigurationValue";
    public void Configure(EntityTypeBuilder<ConfigurationValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(DbConstants.ValueMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ValueMaxLength})")
            .IsUnicode(false);

        builder.Property<Guid>("LocationId");
        builder.Property<Guid>("ConfigurationId");

        builder.HasKey("LocationId", "ConfigurationId")
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.Configuration)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Configuration_ConfigurationId")
            .HasForeignKey("ConfigurationId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Location)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Location_LocationId")
            .HasForeignKey("LocationId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ConfigurationType)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_ConfigurationType_ConfigurationTypeId")
            .HasForeignKey("ConfigurationTypeId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}