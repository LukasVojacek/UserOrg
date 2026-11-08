using Core.Domain.OnboardingRequest;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps the <see cref="OnboardingRequest"/> aggregate to the OnboardingRequest table (ADR-0046).
/// The domain model is free of persistence ids, so the surrogate <c>Id</c>, the <c>ProjectId</c>/<c>UserId</c>
/// FK columns and the audit columns are EF <b>shadow</b> members declared here. The write-adapter
/// (<c>DatabaseContext.OnboardingRequestRepository</c>) assigns <c>Id = Guid.CreateVersion7()</c> and resolves
/// the FK shadows from the natural keys on add — there is no DB <c>NewId()</c> default (mirrors
/// <c>DatabaseContext.ProjectTagHandler</c>).
/// </summary>
public sealed class OnboardingRequestConfiguration : IEntityTypeConfiguration<OnboardingRequest>
{
    public void Configure(EntityTypeBuilder<OnboardingRequest> builder)
    {
        builder.ToTable("OnboardingRequest");

        // Surrogate PK — shadow, app-assigned Guid v7 on add (no DB NewId() default, unlike Request).
        builder.Property<Guid>("Id")
            .HasColumnType("uniqueidentifier");

        builder.HasKey("Id").HasName("PK_OnboardingRequest");

        // Natural domain keys. Project (ProjectShortName) and Requester (IdpId) are not persisted as their
        // own columns — the project/requester are identified by the ProjectId/UserId shadow FKs below — so
        // they are ignored. (ProjectShortName / IdpId remain readable by hydrating via the FKs at query time.)
        builder.Ignore(r => r.Project);
        builder.Ignore(r => r.Requester);

        builder.Property(r => r.ContactEmail)
            .HasConversion(vo => vo.Value, value => Email.Create(value))
            .HasColumnType("nvarchar(256)")
            .IsRequired();

        builder.Property(r => r.State)
            .HasConversion(vo => vo.Value, value => OnboardingRequestState.Create(value))
            .ValueGeneratedOnAdd()
            .HasColumnType("int")
            .HasDefaultValue(OnboardingRequestState.Pending);

        builder.Property(r => r.Slug)
            .HasConversion(
                slug => slug.HasValue ? slug.Value.Value : null,
                value => string.IsNullOrEmpty(value) ? (OnboardingRequestSlug?)null : OnboardingRequestSlug.Create(value))
            .HasColumnType("nvarchar(120)");

        // Shadow FK columns — resolved from the natural keys by the write-adapter on add. Typed as the
        // value-object ids (not raw Guid) so they are compatible with Project/User's value-object shadow PKs.
        builder.Property<ProjectId>("ProjectId")
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.Property<UserId>("UserId")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .HasColumnType("uniqueidentifier")
            .IsRequired();

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

        // Filtered unique index: rows without a slug (none today, but the column is nullable) are excluded;
        // v2 slugs are globally unique (ADR-0024).
        builder.HasIndex("Slug")
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName("IX_OnboardingRequest_Slug");

        // Composite indexes backing the received (per-project) and sent (per-requester) list endpoints.
        builder.HasIndex("ProjectId", "State")
            .HasDatabaseName("IX_OnboardingRequest_ProjectId_State");

        builder.HasIndex("UserId", "State")
            .HasDatabaseName("IX_OnboardingRequest_UserId_State");

        builder.HasOne<Domain.Project.Project>()
            .WithMany()
            .HasForeignKey("ProjectId")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_OnboardingRequest_Project_ProjectId");

        builder.HasOne<Domain.User.User>()
            .WithMany()
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired()
            .HasConstraintName("FK_OnboardingRequest_User_UserId");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}