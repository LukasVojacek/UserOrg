using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ConfigurationTableConfiguration : IEntityTypeConfiguration<Configuration>
{
    private readonly string _tableName = "Configuration";
    public void Configure(EntityTypeBuilder<Configuration> builder)
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

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");
    }
}