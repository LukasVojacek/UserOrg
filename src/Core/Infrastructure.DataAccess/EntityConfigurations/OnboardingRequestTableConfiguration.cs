using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

/// <summary>
/// Legacy EF mapping for the <see cref="OnboardingRequest"/> persistence shim (ADR-0047). Its sole purpose is
/// to make the legacy <c>DatabaseContext</c> migration emit a physical <c>OnboardingRequest</c> table whose
/// shape is byte-for-byte identical to the modular <c>Core.DataAccess.Configurations.OnboardingRequestConfiguration</c>
/// (the runtime source of truth). Column types, nullability, defaults, FK and index names are kept in lockstep
/// with that config; any change there must be reflected here until the modular-canonical cutover (ADR-0047 follow-up).
/// </summary>
public sealed class OnboardingRequestTableConfiguration : IEntityTypeConfiguration<OnboardingRequest>
{
    private readonly string _tableName = "OnboardingRequest";

    public void Configure(EntityTypeBuilder<OnboardingRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        // Surrogate PK — app-assigned Guid v7 on add; deliberately NO DB default (unlike Request/UserProjectAccess).
        builder.Property(x => x.Id)
            .HasColumnType("uniqueidentifier");

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.Property(x => x.ContactEmail)
            .IsRequired()
            .HasColumnType("nvarchar(256)");

        builder.Property(x => x.State)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(0);

        // ADR-0024: GUID-free public addressing slug (nullable; filtered-unique).
        builder.Property(x => x.Slug)
            .HasColumnType("nvarchar(120)");

        builder.Property(x => x.ProjectId)
            .IsRequired()
            .HasColumnType("uniqueidentifier")
            .HasConversion(
                value => value.Value,
                value => ProjectId.Create(value));

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnType("uniqueidentifier");

        builder.Property(x => x.IsDeleted)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasColumnType("bit")
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(x => x.ModifiedAt)
            .ValueGeneratedOnUpdate()
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("null");

        // Filtered unique index on the public slug (rows without a slug are excluded).
        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName($"IX_{_tableName}_Slug");

        // Composite indexes backing the received (per-project) and sent (per-requester) list endpoints.
        builder.HasIndex(nameof(OnboardingRequest.ProjectId), nameof(OnboardingRequest.State))
            .HasDatabaseName($"IX_{_tableName}_ProjectId_State");

        builder.HasIndex(nameof(OnboardingRequest.UserId), nameof(OnboardingRequest.State))
            .HasDatabaseName($"IX_{_tableName}_UserId_State");

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Project_ProjectId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_UserId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}