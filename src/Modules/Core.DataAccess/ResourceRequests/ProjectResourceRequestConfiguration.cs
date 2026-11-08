using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// EF mapping for <see cref="ProjectResourceRequest"/>. Lives in <c>Core.DataAccess.ResourceRequests</c> —
/// NOT in <c>Core.DataAccess.Configurations</c> — so the modular <c>DatabaseContext</c> (which auto-discovers
/// that namespace) never picks it up; it is applied explicitly from <see cref="ResourceRequestsDbContext"/>.
/// The schema comes from the context's default schema (<c>resourceRequests</c>).
/// </summary>
internal sealed class ProjectResourceRequestConfiguration : IEntityTypeConfiguration<ProjectResourceRequest>
{
    // Filter for the single-active invariant (ADR-0066): at most one non-terminal request per key. Kept in
    // sync with ResourceRequestState.NonTerminal (asserted by a unit test).
    internal const string ActiveStateFilter = "[State] IN (N'PendingPiApproval', N'AwaitingResourceCreation')";

    public void Configure(EntityTypeBuilder<ProjectResourceRequest> builder)
    {
        builder.ToTable("ResourceRequests");

        builder.HasKey(x => x.Slug);

        builder.Property(x => x.Slug)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ProjectShortName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ProviderName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ResourceName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.RequesterIdpId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ContactEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.State)
            .HasConversion(s => s.Value, v => ResourceRequestState.From(v))
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .HasMaxLength(128);

        builder.Property(x => x.TokenExpiresAt);

        builder.Property(x => x.WorkflowRequestId)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // Unique slug is the primary key already. Filtered unique index enforces the single-active invariant
        // per (project, provider, resource) — a second non-terminal row for the same key is rejected, while a
        // new row is allowed once the prior one is terminal (ADR-0066).
        builder.HasIndex(x => new { x.ProjectShortName, x.ProviderName, x.ResourceName })
            .IsUnique()
            .HasFilter(ActiveStateFilter);

        // Token validation lookup (ADR-0064): find by hash.
        builder.HasIndex(x => x.TokenHash)
            .HasFilter("[TokenHash] IS NOT NULL");
    }
}