using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the AssignedLocation entity (Provider aggregate) to the AssignedLocation table.
/// Reproduces the existing schema; the LocationId FK, ServiceName, ExternalId and audit columns are
/// shadow properties. The allocated resource is a navigation (<c>_resource</c>) over the SHADOW FK
/// <c>ProjectResourceId</c> (ADR-0031/0041 L2.1) — the column, FK constraint and indexes are unchanged.
/// Id, AllocationUnitTypeId and Mode are mapped domain properties (ADR-0016) over the same columns —
/// no schema change.
/// </summary>
public sealed class AssignedLocationConfiguration : IEntityTypeConfiguration<AssignedLocation>
{
    public void Configure(EntityTypeBuilder<AssignedLocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(a => a.Id)
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey(a => a.Id).HasName("PK_AssignedLocation");

        builder.Property(a => a.AllocationUnitCount)
            .HasColumnType("bigint");

        builder.Property(a => a.Description)
            .HasMaxLength(150)
            .HasColumnType("nvarchar(150)");

        builder.Property(a => a.AggregationName)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.Slug)
            .HasColumnType("nvarchar(120)");

        builder.Property<string>("ExternalId")
            .ValueGeneratedOnAdd()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)")
            .HasDefaultValueSql("null");

        // ValueGeneratedOnAdd: inserts keep the DB default; the domain reads Mode for its guard only.
        builder.Property(a => a.Mode)
            .HasConversion<int>()
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(Core.Domain.Shared.EOperationMode.Unspecified);

        builder.Property<string>("ServiceName")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)")
            .HasDefaultValueSql("'LEXIS'");

        builder.Property(a => a.IsDeleted)
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        builder.Property(a => a.AllocationUnitTypeId)
            .HasColumnType("int");

        builder.Property<Guid>("LocationId")
            .HasColumnType("uniqueidentifier");

        // Shadow FK to the allocated resource — the surrogate stays in storage (ADR-0031/0041 L2.1). The
        // navigation _resource is mapped below; the domain exposes only the resource's natural key.
        builder.Property<ProjectResourceId>("ProjectResourceId")
            .HasConversion(id => id.Value, value => ProjectResourceId.From(value))
            .HasColumnType("uniqueidentifier");

        builder.Property<DateTime>("CreatedAt")
            .ValueGeneratedOnAdd()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("null");

        builder.HasIndex("AllocationUnitTypeId");

        builder.HasIndex("ProjectResourceId");

        builder.HasIndex("LocationId", "ProjectResourceId")
            .HasDatabaseName("IX_AssignedLocation_LocationId_ProjectResourceId");

        // Filtered unique index: v1-created rows keep a null slug (no backfill) and are excluded, so the
        // additive column does not constrain legacy data; v2-generated slugs are globally unique (ADR-0024).
        builder.HasIndex(a => a.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName("IX_AssignedLocation_Slug");

        builder.HasOne<AllocationUnitType>()
            .WithMany()
            .HasForeignKey("AllocationUnitTypeId")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_AssignedLocation_AllocationUnitType_AllocationUnitTypeId");

        builder.HasOne<ProjectResource>("_resource")
            .WithMany()
            .HasForeignKey("ProjectResourceId")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_AssignedLocation_ProjectResource_ProjectResourceId");

        // ClientCascade: ReplaceSpecifications removes loaded values from the collection, and EF must delete
        // those orphans instead of failing on a severed required relationship. The database FK stays NO ACTION.
        builder.HasMany<AssignedLocationValue>("_assignedLocationValues")
            .WithOne()
            .HasForeignKey("AssignedLocationId")
            .OnDelete(DeleteBehavior.ClientCascade)
            .IsRequired()
            .HasConstraintName("FK_AssignedLocationValue_AssignedLocation_AssignedLocationId");

        // Read-only projection over the backing field — not a separate EF navigation.
        builder.Ignore(a => a.Values);

        // Computed projections over the _resource navigation — not stored columns (ADR-0044 L2.1/L2.5).
        builder.Ignore(a => a.ResourceKey);
        builder.Ignore(a => a.ResourceOperationMode);

        builder.ToTable("AssignedLocation");
    }
}