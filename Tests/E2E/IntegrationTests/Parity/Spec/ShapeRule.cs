namespace ParityTests.Spec;

/// <summary>
/// How the comparer should assert a route pair's response shape (ADR-0055 D5).
/// Discriminated union: <see cref="Structural"/> compares only property-name and
/// JSON-kind structure; <see cref="MustMatchFields"/> additionally asserts value
/// equality on the named field paths (ordering keys / natural keys). B05 pattern-matches
/// these; no comparison logic lives here.
/// </summary>
public abstract record ShapeRule;

/// <summary>Structure-only comparison — property-name set and JSON kinds, not scalar values.</summary>
public sealed record Structural : ShapeRule;

/// <summary>
/// Structural comparison plus value equality on the named JSON field paths — used to prove
/// list-ordering / natural-key parity.
/// </summary>
/// <param name="Paths">Field paths whose scalar values must also match across v1 and v2.</param>
public sealed record MustMatchFields(params string[] Paths) : ShapeRule;
