using System.Text.Json.Nodes;

namespace ParityTests.Comparison;

/// <summary>
/// Which side(s) of a v1↔v2 comparison a shape transform is applied to before the
/// structural compare runs.
/// </summary>
public enum ComparisonSide
{
    /// <summary>The legacy v1 response.</summary>
    V1,

    /// <summary>The refactored v2 response.</summary>
    V2,

    /// <summary>Both responses.</summary>
    Both,
}

/// <summary>
/// A structural pre-comparison transform applied to one or both sides to absorb a
/// <em>declared</em> divergence (field rename, value-object-envelope unwrap, …) so the
/// remaining structural compare is apples-to-apples.
/// </summary>
/// <remarks>
/// B03 owns this minimal, self-contained contract on purpose: the richer
/// <c>Divergence</c> model (B04) runs in parallel and may not be merged yet. B04/B05
/// adapt their divergence cases to these transforms rather than B03 taking a hard
/// dependency on B04's types. Transforms operate on <see cref="JsonNode"/> (mutable)
/// and return a fresh, detached node — they never mutate the caller's input.
/// </remarks>
public interface IShapeTransform
{
    /// <summary>The side(s) this transform applies to.</summary>
    ComparisonSide Side { get; }

    /// <summary>Returns a new node tree with the divergence absorbed.</summary>
    JsonNode? Apply(JsonNode? node);
}
