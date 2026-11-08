using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UserOrg.Shared.Hashing;

/// <summary>
/// For every partial type marked <see cref="GenerateIdentityHashAttribute"/>, emits an
/// <see cref="IIdentityHashed"/> implementation whose <c>ComputeIdentityHash</c> canonically
/// renders the type's own public, non-<see cref="HashIgnoreAttribute"/> properties and feeds them
/// to <see cref="IdentityHash.Sha256Hex"/>.
/// </summary>
/// <remarks>
/// The property list is resolved once, here, at compile time — the whole point is that nothing
/// downstream calls <c>Type.GetProperties()</c>/<c>PropertyInfo.GetValue()</c> at run time. A
/// nested property whose OWN type also carries <see cref="GenerateIdentityHashAttribute"/> (checked
/// via the attribute, not <c>IIdentityHashed</c> — the interface does not exist on that symbol yet
/// during this same generation pass) is composed by calling its generated
/// <c>ComputeIdentityHash()</c> instead of being inlined; a collection of such types is composed
/// the same way, element by element.
///
/// A <c>[GenerateIdentityHash]</c> type declared inside another type (the common CQRS
/// request/response-nested-in-endpoint-class shape in this codebase) is supported: every
/// containing type in the chain must itself be a non-generic <c>partial</c> class/struct, and the
/// generated source wraps the leaf partial in matching partial containers (replicating
/// <c>static</c>/<c>sealed</c>/<c>abstract</c> where the containing type needs it for the parts to
/// agree). Generic types — the leaf or any container — are not supported (IDHASH002).
/// </remarks>
[Generator]
public sealed class IdentityHashGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "UserOrg.Shared.Hashing.GenerateIdentityHashAttribute";
    private const string HashIgnoreFullName = "UserOrg.Shared.Hashing.HashIgnoreAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeFullName,
            predicate: static (node, _) => node is RecordDeclarationSyntax or ClassDeclarationSyntax,
            transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        context.RegisterSourceOutput(targets, Emit);
    }

    private static void Emit(SourceProductionContext context, INamedTypeSymbol type)
    {
        var containers = new List<INamedTypeSymbol>();
        for (var container = type.ContainingType; container is not null; container = container.ContainingType)
        {
            containers.Add(container);
        }

        containers.Reverse(); // outermost first

        // Generic types aren't supported anywhere in the chain — check the leaf and every
        // container in one pass before the more specific (and more expensive to explain) checks.
        foreach (var candidate in containers.Prepend(type))
        {
            if (candidate.Arity > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    IdentityHashDiagnostics.GenericTypeNotSupported, type.Locations.FirstOrDefault(), candidate.Name));
                return;
            }
        }

        if (!IsDeclaredPartial(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                IdentityHashDiagnostics.MissingPartial, type.Locations.FirstOrDefault(), type.Name));
            return;
        }

        foreach (var container in containers)
        {
            if (container.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    IdentityHashDiagnostics.UnsupportedContainingTypeKind,
                    type.Locations.FirstOrDefault(), container.Name, container.TypeKind));
                return;
            }

            if (!IsDeclaredPartial(container))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    IdentityHashDiagnostics.ContainingTypeMustBePartial,
                    type.Locations.FirstOrDefault(), container.Name, type.Name));
                return;
            }
        }

        var properties = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.Parameters.Length == 0)
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Where(p => p.GetMethod is not null)
            .Where(p => !HasAttribute(p, HashIgnoreFullName))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        var parts = new List<string>(properties.Length);
        foreach (var property in properties)
        {
            if (!TryRenderExpression(property, out var expression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    IdentityHashDiagnostics.UnsupportedPropertyType,
                    property.Locations.FirstOrDefault(),
                    property.Name,
                    type.Name,
                    property.Type.ToDisplayString()));
                return;
            }

            parts.Add($"\"\\\"{property.Name}\\\":\" + {expression}");
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var canonicalExpression = parts.Count == 0
            ? "\"{}\""
            : $"\"{{\" + {string.Join(" + \",\" + ", parts)} + \"}}\"";

        var leaf = $$"""
            partial {{TypeKindKeyword(type)}} {{type.Name}} : global::UserOrg.Shared.Hashing.IIdentityHashed
            {
                /// <inheritdoc cref="global::UserOrg.Shared.Hashing.IIdentityHashed.ComputeIdentityHash" />
                public string ComputeIdentityHash() =>
                    global::UserOrg.Shared.Hashing.IdentityHash.Sha256Hex({{canonicalExpression}});
            }
            """;

        var body = containers.Count == 0 ? leaf : WrapInContainers(containers, leaf);

        var source = $$"""
            // <auto-generated/>
            #nullable enable

            {{(ns is null ? "" : $"namespace {ns};\n")}}
            {{body}}
            """;

        // Nested types share a metadata-name-safe file name with their containers to stay unique.
        var fileNamePrefix = string.Join(".", containers.Select(c => c.Name).Append(type.Name));
        context.AddSource($"{fileNamePrefix}.IdentityHash.g.cs", source);
    }

    // Wraps `leaf` in a matching partial declaration for each containing type, outermost first, so
    // the generated partial reaches the same nested type the [GenerateIdentityHash] type is
    // declared in. static/sealed/abstract are replicated because C# requires every partial
    // declaration of a type to agree on them.
    private static string WrapInContainers(List<INamedTypeSymbol> containers, string leaf)
    {
        var content = leaf;
        for (var i = containers.Count - 1; i >= 0; i--)
        {
            var container = containers[i];
            var modifiers = ContainerModifiers(container);
            var indented = string.Join("\n", content.Split('\n').Select(line => line.Length == 0 ? line : $"    {line}"));

            content = $$"""
                {{modifiers}}partial {{TypeKindKeyword(container)}} {{container.Name}}
                {
                {{indented}}
                }
                """;
        }

        return content;
    }

    private static string ContainerModifiers(INamedTypeSymbol container)
    {
        if (container.IsStatic)
        {
            return "static ";
        }

        var modifiers = "";
        if (container.IsAbstract)
        {
            modifiers += "abstract ";
        }

        if (container.IsSealed)
        {
            modifiers += "sealed ";
        }

        return modifiers;
    }

    private static string TypeKindKeyword(INamedTypeSymbol type)
    {
        var isStruct = type.TypeKind == TypeKind.Struct;
        return type.IsRecord ? (isStruct ? "record struct" : "record") : (isStruct ? "struct" : "class");
    }

    private static bool TryRenderExpression(IPropertySymbol property, out string expression)
    {
        var access = property.Name;
        var type = property.Type;

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            if (ImplementsGenerateIdentityHash(elementType))
            {
                expression = $"global::UserOrg.Shared.Hashing.CanonicalFormat.HashedList({access})";
                return true;
            }

            if (IsSupportedScalar(elementType))
            {
                expression = $"global::UserOrg.Shared.Hashing.CanonicalFormat.ScalarList({access})";
                return true;
            }

            expression = "";
            return false;
        }

        if (ImplementsGenerateIdentityHash(type))
        {
            expression = $"global::UserOrg.Shared.Hashing.CanonicalFormat.Hashed({access})";
            return true;
        }

        if (IsSupportedScalar(type))
        {
            expression = $"global::UserOrg.Shared.Hashing.CanonicalFormat.Value({access})";
            return true;
        }

        expression = "";
        return false;
    }

    // string implements IEnumerable<char> but must be treated as a scalar, never as a collection.
    private static bool TryGetEnumerableElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type.SpecialType != SpecialType.System_String)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    elementType = iface.TypeArguments[0];
                    return true;
                }
            }
        }

        elementType = null!;
        return false;
    }

    private static bool ImplementsGenerateIdentityHash(ITypeSymbol type) =>
        HasAttribute(type, AttributeFullName);

    private static bool IsSupportedScalar(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return type.SpecialType switch
        {
            SpecialType.System_Boolean or
            SpecialType.System_String or
            SpecialType.System_Char or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Decimal or
            SpecialType.System_Single or
            SpecialType.System_Double => true,
            _ => type.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset",
        };
    }

    private static bool HasAttribute(ISymbol symbol, string attributeFullName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeFullName);

    private static bool IsDeclaredPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(d => d.Modifiers.Any(SyntaxKind.PartialKeyword));
}