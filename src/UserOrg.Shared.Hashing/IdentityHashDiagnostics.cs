using Microsoft.CodeAnalysis;

namespace UserOrg.Shared.Hashing;

internal static class IdentityHashDiagnostics
{
    public static readonly DiagnosticDescriptor MissingPartial = new(
        id: "IDHASH001",
        title: "GenerateIdentityHash type must be partial",
        messageFormat: "'{0}' is marked [GenerateIdentityHash] but is not declared 'partial'",
        category: "UserOrg.Shared.Hashing",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericTypeNotSupported = new(
        id: "IDHASH002",
        title: "GenerateIdentityHash does not support generic types",
        messageFormat: "'{0}' has type parameters, which IdentityHashGenerator does not support (for itself or as a containing type of a [GenerateIdentityHash] type)",
        category: "UserOrg.Shared.Hashing",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "IDHASH003",
        title: "Unsupported property type for identity hashing",
        messageFormat: "Property '{0}' on '{1}' has type '{2}', which IdentityHashGenerator does not know how to hash. Mark it [HashIgnore], implement IIdentityHashed on the type, or add support to CanonicalFormat/IdentityHashGenerator",
        category: "UserOrg.Shared.Hashing",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedContainingTypeKind = new(
        id: "IDHASH004",
        title: "Unsupported containing type kind",
        messageFormat: "'{0}' contains a [GenerateIdentityHash] type but is a {1}, not a class or struct; IdentityHashGenerator cannot emit a matching partial container",
        category: "UserOrg.Shared.Hashing",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        id: "IDHASH005",
        title: "Containing type of a GenerateIdentityHash type must be partial",
        messageFormat: "'{0}' contains a [GenerateIdentityHash] type '{1}' but is not declared 'partial'",
        category: "UserOrg.Shared.Hashing",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}