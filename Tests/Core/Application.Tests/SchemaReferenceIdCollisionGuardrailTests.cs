using System.Reflection;

using Core.Composition;

using UserOrganizationManagement.API.Composition;

namespace UserOrganizationManagement.Application.Tests;

// Reflection guardrail (ADR-0063), complementing OpenApiSchemaReferenceUniquenessTests. That suite inspects the
// generated DOCUMENT and, by its own admission, cannot detect two DISTINCT prefixed ids collapsing
// last-writer-wins onto a single component schema — that merge is invisible in the emitted JSON by
// construction (schema components are per-document, so today's two `GetLocationsEndpoint` classes only avoid
// colliding because they land in different v2 audiences). This test instead applies the REAL
// V2CoreExtensions.SchemaReferenceIdFor delegate-logic directly to EVERY nested type declared, at any nesting
// depth, by every endpoint in the Core feature module — the exact domain the delegate prefixes, not just the
// conventional `Request`/`Response`/`Body` names — across ALL audiences at once, and asserts every produced id
// maps to exactly one distinct CLR type. It goes red for the pre-rename
// `GetLocationsProviderAdmin.GetLocationsEndpoint` / `GetLocations.GetLocationsEndpoint` pair (both produce
// `GetLocationsResponse`) and green once the provider-admin class is renamed (ADR-0063).
public sealed class SchemaReferenceIdCollisionGuardrailTests
{
    [Fact]
    public void Should_MapEachSchemaIdToOneDistinctType_When_ScanningEveryEndpointNestedType()
    {
        var collisions = CollectEndpointNestedTypes()
            .Select(type => (Id: V2CoreExtensions.SchemaReferenceIdFor(type), Type: type))
            .Where(entry => entry.Id is not null)
            .GroupBy(entry => entry.Id!, entry => entry.Type, StringComparer.Ordinal)
            .Select(group => (Id: group.Key, Types: group.Distinct().ToArray()))
            .Where(group => group.Types.Length > 1)
            .ToArray();

        Assert.True(
            collisions.Length == 0,
            $"Schema id collision(s) between distinct CLR types: {string.Join("; ", collisions.Select(collision => $"'{collision.Id}' <- [{string.Join(", ", collision.Types.Select(DescribeType))}]"))}");
    }

    private static string DescribeType(Type type) => $"{type.DeclaringType?.FullName}.{type.Name}";

    // Every nested type declared anywhere inside an `internal static class *Endpoint` in the Core feature
    // module assembly — recursively, since a nested type can itself declare further nested types — matching
    // exactly the set of types SchemaReferenceIdFor prefixes (not just the conventional Request/Response/Body
    // names, e.g. NodeResponse, CommandTemplateResponse, Cluster, LocationAssignment are all in scope too).
    // Compiler-generated nested types (e.g. the synthetic containers backing C# 14 `extension(...)` blocks,
    // named like `<G>$...`) are excluded via the '<' that no valid C# identifier can contain: SchemaReferenceIdFor
    // is only ever invoked by the OpenAPI generator on a JsonTypeInfo for an actually-serialized DTO, which
    // compiler-generated implementation-detail types never are.
    private static IEnumerable<Type> CollectEndpointNestedTypes()
    {
        var coreAssembly = typeof(EndpointsExtensions).Assembly;

        var endpointClasses = coreAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: true, IsSealed: true, DeclaringType: null } &&
                type.Name.EndsWith("Endpoint", StringComparison.Ordinal));

        foreach (var endpointClass in endpointClasses)
        {
            foreach (var nested in AllNestedTypesRecursively(endpointClass))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<Type> AllNestedTypesRecursively(Type type)
    {
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (nested.Name.Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            yield return nested;

            foreach (var deeper in AllNestedTypesRecursively(nested))
            {
                yield return deeper;
            }
        }
    }
}
