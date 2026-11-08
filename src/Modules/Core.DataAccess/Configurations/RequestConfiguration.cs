using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the Request aggregate to the Request table. Reproduces the existing schema exactly,
/// including the cross-aggregate FK constraints to Project, User, ProjectResource and Provider.
/// </summary>
public sealed class RequestConfiguration : IEntityTypeConfiguration<Domain.Request.Request>
{
    public void Configure(EntityTypeBuilder<Domain.Request.Request> builder)
    {
        builder.Property<Guid>("Id")
            .ValueGeneratedOnAdd()
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_Request");

        builder.Property(r => r.ContactEmail)
            .HasConversion<EmailNullableConverter>()
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.Type)
            .HasConversion(vo => vo.Value, value => RequestType.Create(value))
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(RequestType.Default);

        builder.Property(r => r.State)
            .HasConversion(vo => vo.Value, value => RequestState.Create(value))
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(RequestState.Pending);

        builder.Property(r => r.ProjectId)
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.Property(r => r.UserId)
            .HasConversion(id => id.Value, value => UserId.From(value))
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.Property(r => r.ProjectResourceId)
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                value => value.HasValue ? (ProjectResourceId?)ProjectResourceId.From(value.Value) : null)
            .HasColumnType("uniqueidentifier");

        builder.Property(r => r.ProjectResourceName)
            .HasConversion(
                name => name.HasValue ? name.Value.Value : null,
                value => string.IsNullOrEmpty(value) ? null : (ResourceName?)ResourceName.Create(value))
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.ProviderId)
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                value => value.HasValue ? (ProviderId?)ProviderId.From(value.Value) : null)
            .HasColumnType("uniqueidentifier");

        builder.Property(r => r.Slug)
            .HasColumnType("nvarchar(120)");

        // Filtered unique index: v1-created rows keep a null slug (no backfill) and are excluded, so the
        // additive column does not constrain legacy data; v2-generated slugs are globally unique (ADR-0024).
        builder.HasIndex(r => r.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName("IX_Request_Slug");

        builder.Property(r => r.IsDeleted)
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

        builder.HasOne<Domain.Project.Project>()
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_Request_Project_ProjectId");

        builder.HasOne<Domain.User.User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_Request_User_UserId");

        builder.HasOne<Domain.Provider.ProjectResource>()
            .WithMany()
            .HasForeignKey(r => r.ProjectResourceId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Request_ProjectResource_ProjectResourceId");

        builder.HasOne<Domain.Provider.Provider>()
            .WithMany()
            .HasForeignKey(r => r.ProviderId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Request_Provider_ProviderId");

        builder.HasMany<Domain.Request.ActionLink>("_requestActionLinks")
            .WithOne()
            .HasForeignKey("RequestId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .HasConstraintName("FK_ActionLink_Request_RequestId");

        // Read-only projection over the backing field — not a separate EF navigation.
        builder.Ignore(r => r.ActionLinks);

        builder.HasQueryFilter(r => !r.IsDeleted);

        builder.ToTable("Request");
    }
}