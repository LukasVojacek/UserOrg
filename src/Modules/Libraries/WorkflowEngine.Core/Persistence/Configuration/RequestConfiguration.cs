using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Persistence.Configuration;

internal sealed class RequestConfiguration : IEntityTypeConfiguration<RequestAggregate>
{
    public void Configure(EntityTypeBuilder<RequestAggregate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Requests");

        builder.HasKey(r => r.RequestId);
        builder.Property(r => r.RequestId)
            .ValueGeneratedNever();

        builder.Property(r => r.Strategy)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ClientId)
            .HasMaxLength(256);

        builder.Property(r => r.CallbackUrl)
            .HasMaxLength(2000);

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(512);

        builder.Property(r => r.CurrentState)
            .HasMaxLength(200);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(r => r.ErrorCode)
            .HasMaxLength(200);

        builder.Property(r => r.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(r => r.CreatedAt)
            .IsRequired()
            .HasColumnType("datetimeoffset(3)");

        builder.Property(r => r.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetimeoffset(3)");

        builder.Property(r => r.CompletedAt)
            .HasColumnType("datetimeoffset(3)");

        // JsonDocument → nvarchar(max) value converter
        var jsonConverter = BuildJsonConverter();
        var jsonComparer = BuildJsonComparer();
        var nullableJsonConverter = BuildNullableJsonConverter();
        var nullableJsonComparer = BuildNullableJsonComparer();

        builder.Property(r => r.Payload)
            .IsRequired()
            .HasColumnType("nvarchar(max)")
            .HasConversion(jsonConverter, jsonComparer);

        builder.Property(r => r.Result)
            .HasColumnType("nvarchar(max)")
            .HasConversion(nullableJsonConverter!, nullableJsonComparer);

        // Optimistic concurrency via SQL Server rowversion
        builder.Property(r => r.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Navigation to steps — EF uses the private _steps backing field
        builder.HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.RequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Steps)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_steps");

        builder.HasIndex(r => r.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName("IX_Requests_IdempotencyKey");

        builder.HasIndex(r => new { r.Status, r.CreatedAt })
            .HasDatabaseName("IX_Requests_Status_CreatedAt");

        builder.HasIndex(r => new { r.Strategy, r.Status })
            .HasDatabaseName("IX_Requests_Strategy_Status");
    }

    private static ValueConverter<JsonDocument, string> BuildJsonConverter()
        => new(
            v => v.RootElement.GetRawText(),
            v => JsonDocument.Parse(v));

    private static ValueComparer<JsonDocument> BuildJsonComparer()
        => new(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.RootElement.GetRawText() == b.RootElement.GetRawText()),
            v => v == null ? 0 : v.RootElement.GetRawText().GetHashCode(StringComparison.Ordinal),
            v => v == null ? JsonDocument.Parse("null") : JsonDocument.Parse(v.RootElement.GetRawText()));

    private static ValueConverter<JsonDocument?, string?> BuildNullableJsonConverter()
        => new(
            v => v == null ? null : v.RootElement.GetRawText(),
            v => v == null ? null : JsonDocument.Parse(v));

    private static ValueComparer<JsonDocument?> BuildNullableJsonComparer()
        => new(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.RootElement.GetRawText() == b.RootElement.GetRawText()),
            v => v == null ? 0 : v.RootElement.GetRawText().GetHashCode(StringComparison.Ordinal),
            v => v == null ? null : JsonDocument.Parse(v.RootElement.GetRawText()));
}