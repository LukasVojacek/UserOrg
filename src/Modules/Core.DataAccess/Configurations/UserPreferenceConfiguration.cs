
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.Property<Guid>("Id");  // Code-generated UUID v7 (no DB default — ApplyCodeGeneratedGuidKeys)

        builder.HasKey("Id");
        builder.ToTable("UserPreference");

        builder.Property<DateTimeOffset>("CreatedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false)
            .HasDefaultValueSql("null");

        // UserId is required (domain invariant). The deployed column was nullable (legacy slop); the
        // cleanup migration tightens it to NOT NULL (live data has 0 nulls). FK_UserPreference_User_UserId
        // is configured from the User side (UserConfiguration._userPreference, one-to-one → unique index).
        builder.Property(up => up.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired()
            .HasColumnName("UserId");

        // Persistence-only payload column: nvarchar(4000), non-unicode (matches legacy).
        builder.Property(up => up.Data)
            .IsRequired()
            .HasMaxLength(4000)
            .IsUnicode(false)
            .HasColumnType("nvarchar(4000)");

        builder.Property(up => up.IsDeleted)
            .HasDefaultValue(false);
    }
}