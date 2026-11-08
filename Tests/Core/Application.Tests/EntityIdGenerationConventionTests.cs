using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Convention guard for docs/architecture/adr/0061-legacy-write-paths-client-generate-guid-pks.md: the
/// shared <c>ApplyCodeGeneratedGuidKeys()</c> convention is the single source of every Guid primary key on
/// the legacy <see cref="DatabaseContext"/> — it stamps a UUID v7 at <c>Add</c> time
/// (<see cref="ValueGenerated.OnAdd"/>) and the database generates nothing. The three assertions below are
/// one invariant: a key a constructor pre-sets makes EF paint a navigation-discovered entity <c>Modified</c>
/// rather than <c>Added</c> (the 409 of #280), while a key neither pre-set nor generated silently persists
/// all-zeros and collides on the second insert. Production runs on SQL Server; the model here is built
/// against SQLite because it is a relational provider of the same kind. The metadata under test is
/// provider-independent as measured — SqlServer, SQLite and InMemory report identical ValueGenerated across
/// every config shape probed — so the choice buys fidelity of shape, not any additional guarantee.
/// </summary>
public sealed class EntityIdGenerationConventionTests
{
    /// <summary>
    /// Floor for the scanned entity set: the 15 Guid-keyed entities of <see cref="DatabaseContext"/> at the
    /// time of writing. Growth is fine; a drop means the shape filter broke and coverage silently rotted.
    /// </summary>
    private const int KnownGuidKeyedEntityCount = 15;

    [Fact(DisplayName = "Every Guid-keyed entity has a convention-generated primary key")]
    public void GuidKeyedEntities_AreGeneratedOnAdd()
    {
        using var context = BuildSqliteContext();

        var guidKeyed = GuidKeyedEntities(context);
        var offenders = guidKeyed
            .Where(entity => KeyProperty(entity).ValueGenerated != ValueGenerated.OnAdd)
            .Select(entity => $"{entity.ClrType.Name} ({KeyProperty(entity).ValueGenerated})")
            .ToList();

        // Anti-vacuity guard: entities dropping out of the shape filter (Project qualifies only through
        // its value converter, so it goes first) would shrink the scanned set and pass the check for free.
        AssertScansEveryGuidKeyedEntity(guidKeyed);
        Assert.True(
            offenders.Count == 0,
            $"Guid PKs outside ApplyCodeGeneratedGuidKeys()'s ValueGenerated.OnAdd: {string.Join(", ", offenders)}");
    }

    [Fact(DisplayName = "Every Guid-keyed entity leaves its primary key unset at construction")]
    public void GuidKeyedEntities_LeaveTheirIdUnsetAtConstruction()
    {
        using var context = BuildSqliteContext();

        var guidKeyed = GuidKeyedEntities(context);
        var offenders = guidKeyed
            .Where(entity => ConstructedKeyValue(entity) != Guid.Empty)
            .Select(entity => entity.ClrType.Name)
            .ToList();

        // Same anti-vacuity guard as above — see GuidKeyedEntities_AreGeneratedOnAdd.
        AssertScansEveryGuidKeyedEntity(guidKeyed);
        Assert.True(
            offenders.Count == 0,
            $"Constructors that pre-set the primary key: EF then paints an entity discovered through the navigation of a tracked parent Modified and the insert 409s (#280): {string.Join(", ", offenders)}");
    }

    [Fact(DisplayName = "Every Guid-keyed entity is stamped with a GUID v7 id when it is added")]
    public void GuidKeyedEntities_AreStampedWithAV7IdOnAdd()
    {
        using var context = BuildSqliteContext();

        var guidKeyed = GuidKeyedEntities(context);
        var offenders = guidKeyed
            .Select(entity => (Entity: entity.ClrType.Name, Key: AddedKeyValue(context, entity)))
            .Where(stamped => stamped.Key.Version != 7)
            .Select(stamped => $"{stamped.Entity} ({DescribeStampDefect(stamped.Key)})")
            .ToList();

        // Same anti-vacuity guard as above — see GuidKeyedEntities_AreGeneratedOnAdd.
        AssertScansEveryGuidKeyedEntity(guidKeyed);
        Assert.True(
            offenders.Count == 0,
            $"Entities the convention does not stamp with a GUID v7 primary key on Add: {string.Join(", ", offenders)}");
    }

    private static void AssertScansEveryGuidKeyedEntity(List<IEntityType> guidKeyed) =>
        Assert.True(
            guidKeyed.Count >= KnownGuidKeyedEntityCount,
            $"Only {guidKeyed.Count} Guid-keyed entities scanned, expected at least {KnownGuidKeyedEntityCount}: {string.Join(", ", guidKeyed.Select(entity => entity.ClrType.Name))}");

    private static DatabaseContext BuildSqliteContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        return new DatabaseContext(options);
    }

    private static List<IEntityType> GuidKeyedEntities(DatabaseContext context) =>
        [.. context.Model.GetEntityTypes()
            .Where(entity => entity.FindPrimaryKey()?.Properties is [var key] && IsGuidShaped(key))];

    private static IProperty KeyProperty(IEntityType entity) => entity.FindPrimaryKey()!.Properties[0];

    // ponytail: the converter is consulted so Project counts too — its key is a ProjectId stored as a Guid.
    private static bool IsGuidShaped(IProperty property) =>
        (property.GetValueConverter()?.ProviderClrType ?? property.ClrType) == typeof(Guid);

    private static Guid ConstructedKeyValue(IEntityType entity) => KeyValue(entity, Construct(entity));

    private static Guid AddedKeyValue(DatabaseContext context, IEntityType entity)
    {
        var instance = Construct(entity);
        context.Add(instance);
        return KeyValue(entity, instance);
    }

    // Activator.CreateInstance (not `new T()`) is required here: entity.ClrType is only known at
    // runtime, so this is the one construction path that runs every entity's real constructor —
    // catching a type that grew a mint point of its own, generically, without listing types.
    private static object Construct(IEntityType entity) => Activator.CreateInstance(entity.ClrType)!;

    // The converter unwraps a strongly-typed key (ProjectId) down to the Guid it is stored as.
    private static Guid KeyValue(IEntityType entity, object instance)
    {
        var key = KeyProperty(entity);
        var value = key.GetGetter().GetClrValue(instance)!;
        return (Guid)(key.GetValueConverter()?.ConvertToProvider(value) ?? value);
    }

    // An unstamped key is Guid.Empty, whose Version is 0 — caught by the same version check, but it is a
    // different defect (the convention never reached the entity) than a stamp from the wrong Guid factory.
    private static string DescribeStampDefect(Guid key) =>
        key == Guid.Empty ? "left at its default, never stamped" : $"version {key.Version}, expected 7";
}
