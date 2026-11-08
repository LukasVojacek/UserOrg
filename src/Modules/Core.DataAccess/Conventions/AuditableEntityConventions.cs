using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using UserOrg.Shared.Persistance;

namespace Core.DataAccess.Conventions;

/// <summary>
/// Model-wide conventions that reproduce the legacy <c>dbo</c> audit-column schema for the modular
/// <see cref="Contexts.DatabaseContext"/>. The legacy context applied these via
/// <c>UserOrg.Shared.Persistance.ModelBuilderExtensions</c> over its own base types; the modular
/// aggregates use <see cref="Core.Domain.Primitives"/> markers instead, so the same column spec is
/// re-applied here keyed off those markers. Column types/defaults match the legacy snapshot exactly
/// (<c>datetime2(3)</c>, <c>SYSUTCDATETIME()</c>) so the migration cutover stays a zero-diff overlay.
/// </summary>
internal static class AuditableEntityConventions
{
    /// <summary>
    /// Configures the audit columns (<c>CreatedAt</c>, <c>ModifiedAt</c>) and the soft-delete flag
    /// (<c>IsDeleted</c>) for every entity implementing <see cref="Core.Domain.Primitives.ISoftDeletableEntity"/>
    /// — the domain rows. Lookup entities (plain <c>IEntity</c>) are intentionally excluded. The audit
    /// columns may be backed by a shadow <see cref="DateTime"/> or a real <see cref="DateTimeOffset"/>
    /// property (converted); either way only the column type/default is set, never the CLR type.
    /// </summary>
    internal static void ApplyAuditableConventions(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var dateTimeColumnType = $"datetime2({DbConstants.DateTimePrecision})";

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null || clrType.IsAbstract)
                continue;

            if (!typeof(Core.Domain.Primitives.ISoftDeletableEntity).IsAssignableFrom(clrType))
                continue;

            var builder = modelBuilder.Entity(clrType);

            ConfigureAuditColumn(builder, entityType, "CreatedAt", dateTimeColumnType, DbConstants.UtcNowSql, ValueGenerated.OnAdd, required: true);
            ConfigureAuditColumn(builder, entityType, "ModifiedAt", dateTimeColumnType, "null", ValueGenerated.OnUpdate, required: false);

            builder.Property<bool>("IsDeleted")
                .IsRequired()
                .HasDefaultValue(false);
        }
    }

    private static void ConfigureAuditColumn(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder builder,
        IReadOnlyEntityType entityType,
        string name,
        string columnType,
        string defaultValueSql,
        ValueGenerated valueGenerated,
        bool required)
    {
        // Reuse the existing property (shadow DateTime or real DateTimeOffset+converter); only create a
        // shadow property when no config mapped it (DateTime for required CreatedAt, DateTime? for the
        // optional ModifiedAt). This keeps the column DDL identical to legacy without overriding an
        // entity's chosen CLR backing type.
        var existing = entityType.FindProperty(name);
        var property = existing is not null
            ? builder.Property(existing.ClrType, name)
            : required
                ? builder.Property<DateTime>(name)
                : builder.Property<DateTime?>(name);

        property.HasColumnType(columnType).HasDefaultValueSql(defaultValueSql);
        property.Metadata.ValueGenerated = valueGenerated;
        if (required)
            property.IsRequired();
    }
}