using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

namespace Heappe.Persistence.Configurations;

internal sealed class LocationCommandTemplateKillSwitchConfiguration : IEntityTypeConfiguration<LocationCommandTemplateKillSwitch>
{
    private readonly string _tableName = "LocationCommandTemplateKillSwitch";

    public void Configure(EntityTypeBuilder<LocationCommandTemplateKillSwitch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.LocationId)
            .ValueGeneratedNever();

        builder.Property(x => x.IsEnforced)
            .IsRequired();

        builder.Property(x => x.IgnoreWhitelist)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.Reason)
            .IsRequired()
            .HasMaxLength(DbConstants.ExtendedDescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ExtendedDescriptionMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.EnforcedAtUtc)
            .IsRequired()
            .HasColumnType($"datetime2({DbConstants.DateTimePrecision})");

        builder.Property(x => x.EnforcedByUserId)
            .IsRequired();

        builder.Property(x => x.LiftedAtUtc)
            .HasColumnType($"datetime2({DbConstants.DateTimePrecision})");

        builder.Property(x => x.LiftedByUserId)
            .IsRequired(false);

        builder.HasKey(x => x.LocationId)
            .HasName($"PK_{_tableName}");
    }
}