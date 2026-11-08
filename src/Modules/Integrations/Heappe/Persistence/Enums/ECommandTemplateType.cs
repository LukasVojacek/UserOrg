namespace Heappe.Persistence.Enums;

/// <summary>
/// Specifies the source type of a command template.
/// </summary>
/// <remarks>
/// Use this enumeration to distinguish between command templates that are defined by the system, defined
/// by the user, or when the source is unknown. The default value is typically <see
/// cref="ECommandTemplateType.Unknown"/>.
/// </remarks>
internal enum ECommandTemplateType
{
    /// <summary>
    /// Indicates that the value is unknown or has not been specified.
    /// </summary>
    /// <remarks>
    /// Use this value when the actual value is not available or cannot be determined. This is typically the default value for the enumeration.
    /// </remarks>
    Unknown = 0,

    /// <summary>
    /// Indicates that the value is defined by the system.
    /// </summary>
    /// 
    SystemDefined = 1,

    /// <summary>
    /// Indicates that the value is defined by the user.
    /// </summary>
    UserDefined = 2,

    /// <summary>
    /// Represents a generic type or operation.
    /// </summary>
    Generic = 3,
}