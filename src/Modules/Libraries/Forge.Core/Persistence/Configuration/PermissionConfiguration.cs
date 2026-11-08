using Forge.Core.Features.Permission.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using UserOrg.Shared;
using UserOrg.Shared.Persistance;

namespace Forge.Core.Persistence.Configuration;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Permissions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .ValueGeneratedOnAdd();

        var keyConversion = new ValueConverter<PermissionKey, string>(
            v => v.Value,
            v => ParsePermissionKeyFromDatabase(v));

        var keyComparer = new ValueComparer<PermissionKey>(
            (a, b) => StringComparer.Ordinal.Equals(a.Value, b.Value),
            v => StringComparer.Ordinal.GetHashCode(v.Value),
            v => new PermissionKey(v.Value));

        builder.Property(p => p.Key)
            .HasConversion(keyConversion)
            .IsRequired()
            .HasMaxLength(DbConstants.PermissionKeyMaxLength)
            .IsUnicode(false)
            .HasColumnName("Key");

        builder.Property(p => p.Key)
            .Metadata.SetValueComparer(keyComparer);

        builder.Property(p => p.Description)
            .HasMaxLength(DbConstants.ExtendedDescriptionMaxLength)
            .IsUnicode(true)
            .HasColumnType($"nvarchar({DbConstants.ExtendedDescriptionMaxLength})");

        // Shadow property for CreatedAt: not present on CLR model, generated on add by DB
        builder.Property<DateTimeOffset>("CreatedAt")
            .IsRequired()
            .HasColumnType("datetimeoffset(3)")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()")
            .ValueGeneratedOnAdd();

        // Unique index on permission key to prevent duplicates
        builder.HasIndex(p => p.Key)
            .IsUnique()
            .HasDatabaseName("IX_Permissions_Key_Unique");
    }

    private static PermissionKey ParsePermissionKeyFromDatabase(string value)
    {
        if (PermissionKey.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Invalid permission key read from the database: '{value}'");
    }
}