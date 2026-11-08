using System;
using System.Linq;

using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Heappe.UnitTests.Persistence;

/// <summary>
/// Convention guard for #280 across the whole <see cref="HeappeIntegrationDbContext"/> model:
/// every mapped entity with a single Guid primary key mints that key itself at construction and
/// declares it <c>ValueGeneratedNever</c>. The two halves are coupled — a store-generated key
/// makes EF paint a navigation-discovered entity <c>Modified</c> (the 409), while a
/// never-generated key with no mint point silently persists the all-zeros Guid and collides on
/// the second insert.
/// </summary>
public sealed class EntityIdGenerationConventionTests
{
    // LocationCommandTemplateKillSwitch is keyed by LocationId — the id of the Location it freezes,
    // supplied by the caller rather than owned by the row. It has no id of its own to mint and is
    // deliberately already ValueGeneratedNever (LocationCommandTemplateKillSwitchConfiguration.cs:20).
    private static readonly string[] ExcludedEntityTypes = [nameof(LocationCommandTemplateKillSwitch)];

    // LocationCommandTemplateWhitelist is absent from both this list and the exclusion list on
    // purpose: its key is composite (LocationId, ProjectCommandTemplateId), so the enumeration
    // below never offers it as a single-Guid-PK candidate in the first place.
    private static readonly string[] GuardedEntityTypes =
    [
        nameof(CommandTemplateDefinition),
        nameof(CommandTemplateLocationDefinition),
        nameof(CommandTemplateParameterDefinition),
        nameof(LocationPartition),
        nameof(ProjectCommandTemplate),
        nameof(ProjectCommandTemplatePartition),
    ];

    [Fact(DisplayName = "The model sweep covers every Guid-keyed Heappe entity")]
    public void Sweep_covers_the_guid_keyed_entities()
    {
        var swept = GuidKeyedPrimaryKeys().Select(key => key.DeclaringType.ClrType.Name).ToArray();

        var missing = GuardedEntityTypes.Except(swept).ToArray();
        Assert.True(
            missing.Length == 0,
            $"The sweep no longer reaches these Guid-keyed entities, so the assertions below would pass vacuously: {string.Join(", ", missing)}");
    }

    [Fact(DisplayName = "Every Guid-keyed Heappe entity is configured client-generated")]
    public void Every_guid_keyed_entity_is_configured_client_generated()
    {
        var storeGenerated = GuidKeyedPrimaryKeys()
            .Where(key => key.ValueGenerated != ValueGenerated.Never)
            .Select(key => $"{key.DeclaringType.ClrType.Name}.{key.Name} ({key.ValueGenerated})")
            .ToArray();

        Assert.True(
            storeGenerated.Length == 0,
            $"These Guid primary keys are still store-generated; EF then paints a navigation-discovered entity Modified and the insert 409s (#280): {string.Join(", ", storeGenerated)}");
    }

    [Fact(DisplayName = "Every Guid-keyed Heappe entity mints a v7 id at construction")]
    public void Every_guid_keyed_entity_mints_its_id_at_construction()
    {
        var offenders = GuidKeyedPrimaryKeys()
            .Select(key => (Key: key, MintedId: (Guid)key.PropertyInfo!.GetValue(Construct(key.DeclaringType.ClrType))!))
            .Where(minted => minted.MintedId.Version != 7)
            .Select(minted => $"{minted.Key.DeclaringType.ClrType.Name}.{minted.Key.Name} — {DescribeDefect(minted.MintedId)}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These entities do not mint a Guid.CreateVersion7() key at construction: {string.Join("; ", offenders)}");
    }

    private static string DescribeDefect(Guid mintedId) => mintedId == Guid.Empty
        ? "constructs empty; under ValueGeneratedNever the row persists all-zeros and the next insert collides on the PK"
        : $"constructs a v{mintedId.Version} id, which is not time-ordered, violating the client-generated v7 convention";

    // The entities are internal and so are their constructors; nonPublic reaches the same
    // parameterless constructor EF materialises with, including one the compiler emitted
    // implicitly — the hole that left ProjectCommandTemplate mint-less in B05's first attempt.
    private static object Construct(Type entityType) => Activator.CreateInstance(entityType, nonPublic: true)!;

    private static IProperty[] GuidKeyedPrimaryKeys()
    {
        // SQLite rather than the InMemory provider the rest of this project uses, for fidelity: it
        // is a relational provider like the one production runs on, so the model under test is
        // assembled the same way it is at runtime. That is the whole reason — measured on EF Core
        // 10, InMemory reports ValueGenerated identically across all four configuration shapes
        // tried (ValueGeneratedNever / bare HasDefaultValueSql / HasDefaultValueSql +
        // ValueGeneratedOnAdd / no Id configuration at all), so the assertion below would not be
        // invalid under InMemory either.
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var dbContext = new HeappeIntegrationDbContext(options);

        return dbContext.Model.GetEntityTypes()
            .Where(entityType => !ExcludedEntityTypes.Contains(entityType.ClrType.Name))
            .Select(entityType => entityType.FindPrimaryKey())
            .Where(key => key is { Properties.Count: 1 })
            .Select(key => key!.Properties[0])
            .Where(property => property.ClrType == typeof(Guid))
            .ToArray();
    }
}
