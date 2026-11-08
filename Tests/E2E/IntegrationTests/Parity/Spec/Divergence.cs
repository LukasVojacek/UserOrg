namespace ParityTests.Spec;

/// <summary>
/// A declared, intended v1↔v2 difference tolerated during comparison (ADR-0055 D4 allow-list).
/// Discriminated union limited to the cases the plan's allow-list names — no premature cases.
/// This is a pure data model: B05 bridges each case to B03's <c>JsonShapeComparer</c> transforms;
/// no comparer logic lives here.
/// </summary>
public abstract record Divergence;

/// <summary>A property renamed between stacks (e.g. <c>shortName</c> → <c>name</c>).</summary>
/// <param name="From">v1 property name.</param>
/// <param name="To">v2 property name.</param>
public sealed record FieldRenamed(string From, string To) : Divergence;

/// <summary>A v1 property with no v2 counterpart (e.g. the enum <c>Id</c> v2 no longer exposes).</summary>
/// <param name="Name">v1 property name.</param>
public sealed record FieldRemoved(string Name) : Divergence;

/// <summary>
/// A property both stacks expose whose JSON kind changed: v1 renders a lookup as its number, v2 as its
/// stable string name (ADR-0020 §4, e.g. <c>RequestState</c>). Presence is still compared.
/// </summary>
/// <param name="Name">Property name on both stacks.</param>
public sealed record FieldKindChanged(string Name) : Divergence;

/// <summary>
/// A value-object rendered bare on one stack and enveloped as <c>{"Value":x}</c> on the other,
/// at the given path (precedent: <c>ReadProjectShortName</c>).
/// </summary>
/// <param name="Path">Field path of the enveloped value object.</param>
public sealed record ValueObjectEnvelope(string Path) : Divergence;

/// <summary>An intended HTTP status difference between the two stacks for the same request.</summary>
/// <param name="V1">Status returned by the legacy route.</param>
/// <param name="V2">Status returned by the v2 route.</param>
public sealed record StatusRemap(HttpStatusCode V1, HttpStatusCode V2) : Divergence;
