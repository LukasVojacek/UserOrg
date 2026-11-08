namespace UserOrg.Shared.Persistance.Abstractions.Enums;

/// <summary>
/// Specifies the available operation modes for a component or process.
/// </summary>
/// <remarks>Use this enumeration to indicate whether an operation should be performed in read-only mode, editable
/// mode, or if the mode is unknown. The meaning of each mode may vary depending on the context in which it is
/// used.</remarks>
public enum EOperationMode
{
    /// <summary>
    /// Indicates that the value is unspecified or has not been specified.
    /// </summary>
    /// <remarks>Use this value when the actual value is not available or cannot be determined. This is
    /// typically the default value for the enumeration.</remarks>
    Unspecified = 0,

    /// <summary>
    /// Specifies that the resource is accessible in read-only mode.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// Indicates that the item can be modified by the user.
    /// </summary>
    Editable = 2
}