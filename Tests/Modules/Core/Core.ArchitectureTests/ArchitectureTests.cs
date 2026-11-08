using System.Reflection;

using Core.Domain.Primitives;

namespace Core.ArchitectureTests;

/// <summary>
/// Codifies the architecture standard against the real Core module assemblies via reflection.
/// Rules enforced: dependency direction, strongly-typed aggregate ids, persistence ignorance.
/// </summary>
public sealed class CoreArchitectureTests
{
    // Anchored to a public marker type so Core.Domain is loaded. Core.DataAccess exposes no
    // public marker type (its DbContext is internal), so it is loaded by simple name instead.
    private static readonly Assembly DomainAssembly = typeof(IEntity).Assembly;
    private static readonly Assembly DataAccessAssembly = Assembly.Load("Core.DataAccess");

    // ---- Rule 1: dependency direction (the chain is one-way and acyclic) ----
    // This reflects the assemblies that are actually used. The compiler trims unused references,
    // so the check catches real type-level coupling. A project reference that is added but never
    // used will not surface here, so the csproj reference remains the backstop for that case.

    [Fact]
    public void Should_NotReferenceOuterLayers_When_AssemblyIsDomain()
    {
        var forbidden = new[] { "Core.DataAccess", "Core" };
        var references = ReferencedNames(DomainAssembly);

        var violations = forbidden.Where(references.Contains).ToArray();

        Assert.True(
            violations.Length == 0,
            $"Core.Domain must not reference outer layers, but references: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Should_NotReferenceUseCaseLayer_When_AssemblyIsDataAccess()
    {
        // Exact "Core" is the use-case assembly; "Core.Domain" / "Forge.Core" are allowed.
        Assert.False(
            ReferencedNames(DataAccessAssembly).Contains("Core"),
            "Core.DataAccess must not reference the Core use-case assembly.");
    }

    // ---- Rule 2: strongly-typed ids on aggregate roots ----
    // Aggregate identity is a typed id value object or an EF shadow key, never a raw primitive.
    // Scope note: legacy reference/lookup entities (plain IEntity) and raw foreign-key properties
    // still carry int/Guid keys; tightening to every entity is the upgrade path, not enforced here.

    [Fact]
    public void Should_NotExposeRawPrimitiveId_When_TypeIsAggregateRoot()
    {
        var rawPrimitives = new HashSet<Type>
        {
            typeof(Guid), typeof(int), typeof(long), typeof(short),
            typeof(byte), typeof(uint), typeof(ulong), typeof(string),
        };

        var violations = ConcreteTypes(DomainAssembly)
            .Where(t => typeof(AggregateRoot).IsAssignableFrom(t))
            .Select(t => (Type: t, Id: t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)))
            .Where(x => x.Id is not null)
            .Select(x => (x.Type, IdType: Nullable.GetUnderlyingType(x.Id!.PropertyType) ?? x.Id.PropertyType))
            .Where(x => rawPrimitives.Contains(x.IdType))
            .Select(x => $"{x.Type.Name}.Id : {x.IdType.Name}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Aggregate roots must use strongly-typed ids (or EF shadow keys), not raw primitives: "
            + string.Join(", ", violations));
    }

    // ---- Rule 3: persistence ignorance ----
    // Domain types carry no EF Core / DataAnnotations attributes; mapping lives in IEntityTypeConfiguration<T>.

    [Fact]
    public void Should_NotCarryPersistenceAttributes_When_TypeIsInDomain()
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var violations = new List<string>();
        foreach (var type in DomainAssembly.GetTypes())
        {
            violations.AddRange(type.GetCustomAttributesData()
                .Where(IsPersistenceAttribute)
                .Select(a => $"{type.Name} <- [{a.AttributeType.Name}]"));

            foreach (var member in type.GetMembers(members))
            {
                violations.AddRange(member.GetCustomAttributesData()
                    .Where(IsPersistenceAttribute)
                    .Select(a => $"{type.Name}.{member.Name} <- [{a.AttributeType.Name}]"));
            }
        }

        Assert.True(
            violations.Count == 0,
            "Domain types must be persistence-ignorant (no EF / DataAnnotations attributes): "
            + string.Join(", ", violations));
    }

    private static HashSet<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false });

    private static bool IsPersistenceAttribute(CustomAttributeData attribute)
    {
        var ns = attribute.AttributeType.Namespace ?? string.Empty;
        return ns.StartsWith("System.ComponentModel.DataAnnotations", StringComparison.Ordinal)
            || ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal);
    }
}
