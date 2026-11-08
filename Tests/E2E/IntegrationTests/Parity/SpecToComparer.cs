using ParityTests.Comparison;
using ParityTests.Spec;

namespace ParityTests;

/// <summary>
/// Bridges the declarative B04 <see cref="Divergence"/> allow-list to the B03
/// <see cref="IShapeTransform"/> engine, so a parity spec's <c>Known</c> divergences drive the
/// structural compare without either block depending on the other's concrete types.
/// </summary>
/// <remarks>
/// <para>
/// Mapping (v1 is the legacy stack, v2 the refactored one):
/// <list type="bullet">
///   <item><see cref="FieldRenamed"/> → <see cref="FieldRenameTransform"/> on <see cref="ComparisonSide.V1"/>:
///     rename v1's property to v2's name so both sides share one vocabulary.</item>
///   <item><see cref="FieldRemoved"/> → <see cref="FieldRemoveTransform"/> on <see cref="ComparisonSide.V1"/>:
///     drop the v1 property v2 no longer exposes.</item>
///   <item><see cref="FieldKindChanged"/> → <see cref="FieldKindChangeTransform"/> on <see cref="ComparisonSide.V1"/>:
///     render v1's number as a string so it meets v2's name; a v2 number or a missing property still mismatches.</item>
///   <item><see cref="ValueObjectEnvelope"/> → <see cref="ValueObjectEnvelopeTransform"/> on
///     <see cref="ComparisonSide.V1"/>: v1 serialises a value object enveloped
///     (<c>{ "Value": x }</c>); unwrap it on v1 to match bare v2 (precedent: <c>ReadProjectShortName</c>).</item>
///   <item><see cref="StatusRemap"/> → no body transform; it is asserted at the HTTP-status-compare
///     step (<see cref="FindStatusRemap"/>).</item>
/// </list>
/// </para>
/// <para>Reused by B06/B07/B08, so the mapping lives here once rather than per test class.</para>
/// </remarks>
internal static class SpecToComparer
{
    /// <summary>
    /// Maps the body-shape divergences to their comparer transforms. <see cref="StatusRemap"/> is
    /// deliberately excluded — it governs status parity, not response shape.
    /// </summary>
    public static IReadOnlyList<IShapeTransform> ToTransforms(IEnumerable<Divergence> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        return [.. known.Select(ToTransform).OfType<IShapeTransform>()];
    }

    /// <summary>
    /// The declared HTTP-status divergence for the pair, or <see langword="null"/> when both stacks
    /// are expected to return the same status.
    /// </summary>
    public static StatusRemap? FindStatusRemap(IEnumerable<Divergence> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        return known.OfType<StatusRemap>().FirstOrDefault();
    }

    private static IShapeTransform? ToTransform(Divergence divergence) => divergence switch
    {
        FieldRenamed(var from, var to) => new FieldRenameTransform(from, to, ComparisonSide.V1),
        FieldRemoved(var name) => new FieldRemoveTransform(name, ComparisonSide.V1),
        FieldKindChanged(var name) => new FieldKindChangeTransform(name, ComparisonSide.V1),
        ValueObjectEnvelope(var path) => new ValueObjectEnvelopeTransform(path, ComparisonSide.V1),
        StatusRemap => null,
        _ => throw new NotSupportedException($"No comparer transform is defined for divergence '{divergence.GetType().Name}'."),
    };
}
