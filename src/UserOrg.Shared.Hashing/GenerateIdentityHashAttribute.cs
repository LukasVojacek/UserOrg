namespace UserOrg.Shared.Hashing;

/// <summary>
/// Marks a partial record/class for compile-time generation of <see cref="IIdentityHashed"/> —
/// a short, stable, content-derived identifier (SHA-256 over a canonical rendering of its public
/// properties). Replaces walking the type with reflection at run time: the property list is
/// resolved once, at build time, by <c>IdentityHashGenerator</c>.
/// </summary>
/// <remarks>
/// Properties marked <see cref="HashIgnoreAttribute"/> are excluded. A nested property whose type
/// itself carries this attribute is composed by its own <see cref="IIdentityHashed.ComputeIdentityHash"/>
/// rather than being inlined — same for a collection of such types.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class GenerateIdentityHashAttribute : Attribute;