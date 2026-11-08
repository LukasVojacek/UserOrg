using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Outbox;

/// <summary>
/// EF mapping for <see cref="OutboxMessage"/>. Lives in <c>Core.DataAccess.Outbox</c> — NOT in
/// <c>Core.DataAccess.Configurations</c> — so the modular <c>DatabaseContext</c> (which auto-discovers
/// that namespace) does not pick it up. It is applied explicitly from <see cref="OutboxDbContext"/>.
/// The schema comes from the context's default schema (<c>outbox</c>).
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Outbox");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.Property(x => x.DispatchedAt);

        builder.Property(x => x.Attempts)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.LastAttemptAt);

        builder.Property(x => x.Error)
            .HasColumnType("nvarchar(max)");

        // Dispatcher polls for undispatched rows; a filtered index keeps that scan cheap.
        builder.HasIndex(x => x.DispatchedAt)
            .HasFilter("[DispatchedAt] IS NULL");
    }
}