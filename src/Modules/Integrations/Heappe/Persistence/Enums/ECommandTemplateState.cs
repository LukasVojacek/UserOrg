namespace Heappe.Persistence.Enums;

/// <summary>
/// Specifies the status of a command template in the HEAppE system.
/// </summary>
/// <remarks>
/// Use this enumeration to determine the current lifecycle state of a command template, such as whether
/// it is pending initialization, active, inactive, or in an unknown state. The status can be used to control workflow
/// logic or to display appropriate information to users.
/// </remarks>
public enum ECommandTemplateState
{
    /// <summary>
    /// Indicates that the value is unknown or has not been specified.
    /// </summary>
    /// <remarks>
    /// This value is typically used as a default or uninitialized state. It may also be returned
    /// when the actual value cannot be determined.
    /// </remarks>
    Unknown = 0,

    /// <summary>
    /// Indicate that the Command Template was created but not yet initialized in HEAppE side.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Indicates that the Command Template has been initialized in HEAppE side and is ready for use.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Indicates that the Command Template is inactive.
    /// </summary>
    Inactive = 3
}