using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

namespace Heappe.Persistence.Configurations;

internal sealed class LocationCommandTemplateWhitelistConfiguration : IEntityTypeConfiguration<LocationCommandTemplateWhitelist>
{
    private readonly string _tableName = "LocationCommandTemplateWhitelist";

    public void Configure(EntityTypeBuilder<LocationCommandTemplateWhitelist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.AddedByUserId)
            .IsRequired();

        builder.Property(x => x.AddedAtUtc)
            .IsRequired()
            .HasColumnType($"datetime2({DbConstants.DateTimePrecision})");

        builder.HasKey(x => new { x.LocationId, x.ProjectCommandTemplateId })
            .HasName($"PK_{_tableName}");
    }
}