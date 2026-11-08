
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

public class ProjectTagConfiguration : IEntityTypeConfiguration<ProjectTag>
{
    public void Configure(EntityTypeBuilder<ProjectTag> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ProjectTag");

        // Shadow primary key (domain ProjectTag has no Id)
        builder.Property<Guid>("Id")
            .HasColumnName("Id")
            .ValueGeneratedNever(); // Id generated in domain code

        builder.HasKey("Id");

        builder.Property(pt => pt.Name)
            .IsRequired()
            .HasMaxLength(100)
            .HasConversion(
                v => v.Value,
                v => new TagName(v));

        // Persistence-only column present in the deployed schema (no domain property).
        builder.Property<string>("Description")
            .IsRequired(false)
            .HasMaxLength(150)
            .IsUnicode(true)
            .HasColumnType("nvarchar(150)");

        // Audit timestamps: domain exposes DateTimeOffset, legacy columns are datetime (UTC).
        // Without this converter, reading an existing ProjectTag row throws
        // ("expected DateTimeOffset but actual value was DateTime"). Mirrors ProjectConfiguration.
        builder.Property(pt => pt.CreatedAt)
            .HasConversion<UtcDateTimeOffsetConverter>();

        builder.Property(pt => pt.ModifiedAt)
            .HasConversion<UtcDateTimeOffsetConverter>();

        // DDD ideal: CreatedByUserId should store UserName (natural key of User aggregate),
        // removing the coupling to User's surrogate Guid PK.
        // Requires DB migration: ALTER TABLE ProjectTag ALTER COLUMN CreatedByUserId NVARCHAR(255),
        // backfill: UPDATE pt SET CreatedByUserId = u.UserName FROM ProjectTag pt JOIN [User] u ON u.Id = pt.CreatedByUserId,
        // then update the FK to reference User.UserName (alternate key).
        // Current: shadow Guid FK used for DB referential integrity; CreatedBy (UserName) is not populated from DB.
        builder.Property<UserId>("CreatedByUserId")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .IsRequired();

        builder.HasIndex("CreatedByUserId")
            .HasDatabaseName("IX_ProjectTag_CreatedByUserId");

        builder.HasOne<Domain.User.User>()
            .WithMany()
            .HasForeignKey("CreatedByUserId")
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(pt => pt.CreatedBy);

        // The deployed schema has no key or unique index on Name (the legacy tag write path allows
        // duplicate names). A filtered unique index is deferred until the legacy tag writes are retired
        // (see docs/ddd-migration).
    }
}