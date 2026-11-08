using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the ActionLink child entity to the ActionLink table. Reproduces the existing schema.
/// Id, RequestId and audit columns are shadow properties; the FK is configured from <see cref="RequestConfiguration"/>.
/// </summary>
public sealed class ActionLinkConfiguration : IEntityTypeConfiguration<ActionLink>
{
    public void Configure(EntityTypeBuilder<ActionLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<Guid>("Id")
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_ActionLink");

        builder.Property<Guid>("RequestId")
            .HasColumnType("uniqueidentifier");

        builder.Property(al => al.Action)
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(0);

        builder.Property(al => al.Link)
            .HasColumnType("nvarchar(max)");

        builder.Property(al => al.ValidTo)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2");

        builder.Property(al => al.IsDeleted)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        builder.Property<DateTime>("CreatedAt")
            .ValueGeneratedOnAdd()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("null");

        builder.ToTable("ActionLink");
    }
}