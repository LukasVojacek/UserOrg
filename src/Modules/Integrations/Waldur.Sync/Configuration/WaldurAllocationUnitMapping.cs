namespace Waldur.Sync.Configuration;

/// <summary>
/// One Waldur→Userorg allocation-unit conversion rule (legacy <c>AllocationUnitConverter</c>): the
/// Waldur measured-unit key, the Userorg unit-type name, and the integer multiplier applied to the
/// raw allocation amount.
/// </summary>
public sealed record WaldurAllocationUnitMapping(string WaldurKey, string UserorgKey, int Multiplier)
{
    /// <summary>Converts a raw Waldur allocation amount to Userorg units (legacy <c>ConvertToLexisUnit</c>).</summary>
    public long ConvertToUserorgUnit(decimal waldurAllocationValue) => (long)waldurAllocationValue * Multiplier;

    /// <summary>The three standard mappings shared by every instance today (EFP + Puhuri constants were identical).</summary>
    public static IList<WaldurAllocationUnitMapping> Defaults() =>
    [
        new("Node-hours", "node-hours", 1),
        new("k-Hours", "core-hours", 1000),
        new("TB-hours", "GB", 1000)
    ];
}