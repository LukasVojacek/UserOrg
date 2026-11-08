using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the ProjectResource entity to the ProjectResource table. Reproduces the existing schema exactly.
/// </summary>
public sealed class ProjectResourceConfiguration : IEntityTypeConfiguration<ProjectResource>
{
    public void Configure(EntityTypeBuilder<ProjectResource> builder)
    {
        builder.Property<ProjectResourceId>("Id")
            .HasConversion(id => id.Value, value => ProjectResourceId.From(value))
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_ProjectResource");

        builder.Property(pr => pr.ResourceName)
            .HasConversion(rn => rn.Value, value => ResourceName.Create(value))
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)")
            .HasColumnName("Name");

        // Owning provider (ADR-0026 / ADR-0031). Shadow FK on Provider.Id — the surrogate stays in storage; the
        // domain exposes only ProviderName through the _provider navigation. Nullable column — legacy standalone
        // rows carry NULL. The physical FK_ProjectResource_Provider_ProviderId enforces referential integrity.
        builder.Property<ProviderId?>("ProviderId")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? ProviderId.From(value.Value) : (ProviderId?)null)
            .HasColumnType("uniqueidentifier")
            .HasColumnName("ProviderId");

        builder.HasOne<Domain.Provider.Provider>("_provider")
            .WithMany()
            .HasForeignKey("ProviderId")
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProjectResource_Provider_ProviderId");

        builder.Property(pr => pr.Description)
            .HasMaxLength(150)
            .HasColumnType("nvarchar(150)");

        builder.Property(pr => pr.StartDate)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .IsRequired();

        builder.Property(pr => pr.EndDate)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .IsRequired();

        builder.Property(pr => pr.PrimaryInvestigatorEmail)
            .HasConversion<EmailNullableConverter>()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)");

        builder.Property(pr => pr.ExternalId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : null,
                value => string.IsNullOrEmpty(value) ? (ExternalId?)null : ExternalId.Create(value))
            .ValueGeneratedOnAdd()
            .HasMaxLength(200)
            .HasColumnType("nvarchar(200)")
            .HasDefaultValueSql("null");

        builder.Property(pr => pr.ServiceName)
            .HasConversion(sn => sn.Value, value => ServiceName.Create(value))
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasMaxLength(100)
            .HasColumnType("nvarchar(100)")
            .HasDefaultValueSql("'LEXIS'");

        builder.Property(pr => pr.OperationMode)
            .HasConversion(mode => (int)mode, value => (EOperationMode)value)
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasColumnName("Mode")
            .HasDefaultValue(EOperationMode.Unspecified);

        builder.Property(pr => pr.IsDeleted)
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

        builder.HasIndex(pr => pr.ResourceName)
            .HasDatabaseName("IX_ProjectResource_Name");

        // ADR-0026: a resource belongs to exactly one provider and is unique by (ProviderId, Name). At most one
        // ACTIVE row may carry a given name under a provider; the same name may exist under another provider.
        // Filtered to !IsDeleted (soft-deleted history never blocks a re-add) and ProviderId IS NOT NULL (legacy
        // standalone rows carry NULL and are exempt). Reproduces the physical IX_ProjectResource_ProviderId_Name
        // (legacy migration 20260627143547). Supersedes the never-physically-present (ServiceName, Name) index.
        builder.HasIndex("ProviderId", nameof(Domain.Provider.ProjectResource.ResourceName))
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ProviderId] IS NOT NULL")
            .HasDatabaseName("IX_ProjectResource_ProviderId_Name");

        // Computed natural-key projection over the _provider navigation — not a stored column (ADR-0044 §1).
        builder.Ignore(pr => pr.Key);

        builder.HasQueryFilter(pr => !pr.IsDeleted);

        builder.Property(p => p.CreatedBy)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? UserId.From(value.Value) : (UserId?)null)
            .HasColumnName("CreatedByUserId");

        builder.HasIndex(p => p.CreatedBy)
            .HasDatabaseName("IX_ProjectResource_CreatedByUserId");

        builder.HasOne<Domain.User.User>()
            .WithMany()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProjectResource_User_CreatedByUserId");

        builder.ToTable("ProjectResource");
    }
}