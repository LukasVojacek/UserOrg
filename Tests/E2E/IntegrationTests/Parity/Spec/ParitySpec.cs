namespace ParityTests.Spec;

/// <summary>
/// One declarative entry in the v1↔v2 parity spec (ADR-0055 D4). Names a v1 read route,
/// its v2 counterpart, the roles to run under, the shape rule to assert, and the declared
/// intended divergences to tolerate. Consumed by the data-driven parity theories (B05/B06);
/// this block defines the model only — no route data, no client wiring, no comparer logic.
/// </summary>
/// <param name="Name">Human-readable label for the route pair (used as the theory case id).</param>
/// <param name="V1Path">Legacy route, e.g. <c>/api/Provider</c> (seeded params already substituted).</param>
/// <param name="V2Path">v2 counterpart, e.g. <c>/api/v2/Providers</c> (seeded params already substituted).</param>
/// <param name="Roles">Which fixture dev-auth clients to exercise the pair under.</param>
/// <param name="Shape">Structural-only, or must-match on named field paths.</param>
/// <param name="Known">Declared intended divergences tolerated during comparison.</param>
public sealed record ParitySpec(
    string Name,
    string V1Path,
    string V2Path,
    Role[] Roles,
    ShapeRule Shape,
    Divergence[] Known);
