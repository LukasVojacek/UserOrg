using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Persistence.Configuration;

internal sealed class RequestStepConfiguration : IEntityTypeConfiguration<RequestStep>
{
    public void Configure(EntityTypeBuilder<RequestStep> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RequestSteps");

        builder.HasKey(s => s.StepId);
        builder.Property(s => s.StepId)
            .ValueGeneratedNever();

        builder.Property(s => s.RequestId)
            .IsRequired();

        builder.Property(s => s.StepName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.ExecutionStatus)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(s => s.MessageType)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(s => s.StepRetries)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.LockedBy)
            .HasMaxLength(200);

        builder.Property(s => s.ErrorCode)
            .HasMaxLength(200);

        builder.Property(s => s.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(s => s.StartedAt)
            .HasColumnType("datetimeoffset(3)");

        builder.Property(s => s.CompletedAt)
            .HasColumnType("datetimeoffset(3)");

        builder.Property(s => s.NextRetryAt)
            .HasColumnType("datetimeoffset(3)");

        builder.Property(s => s.LockedUntil)
            .HasColumnType("datetimeoffset(3)");

        // JsonDocument? → nvarchar(max) value converter
        var nullableJsonConverter = new ValueConverter<JsonDocument?, string?>(
            v => v == null ? null : v.RootElement.GetRawText(),
            v => v == null ? null : JsonDocument.Parse(v));

        var nullableJsonComparer = new ValueComparer<JsonDocument?>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.RootElement.GetRawText() == b.RootElement.GetRawText()),
            v => v == null ? 0 : v.RootElement.GetRawText().GetHashCode(StringComparison.Ordinal),
            v => v == null ? null : JsonDocument.Parse(v.RootElement.GetRawText()));

        builder.Property(s => s.InputData)
            .HasColumnType("nvarchar(max)")
            .HasConversion(nullableJsonConverter!, nullableJsonComparer);

        builder.Property(s => s.Result)
            .HasColumnType("nvarchar(max)")
            .HasConversion(nullableJsonConverter!, nullableJsonComparer);

        builder.HasIndex(s => new { s.RequestId, s.StepName })
            .HasDatabaseName("IX_RequestSteps_RequestId_StepName");

        builder.HasIndex(s => new { s.ExecutionStatus, s.NextRetryAt })
            .HasFilter("[ExecutionStatus] = 'RetryScheduled'")
            .HasDatabaseName("IX_RequestSteps_RetryScheduled_NextRetryAt");

        builder.HasIndex(s => new { s.ExecutionStatus, s.LockedUntil })
            .HasFilter("[ExecutionStatus] = 'Running'")
            .HasDatabaseName("IX_RequestSteps_Running_LockedUntil");
    }
}