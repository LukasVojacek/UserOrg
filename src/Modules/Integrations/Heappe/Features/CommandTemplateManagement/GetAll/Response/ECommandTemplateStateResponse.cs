using System.Text.Json.Serialization;

namespace Heappe.Features.CommandTemplateManagement.GetAll.Response;

/// <summary>
/// Represents the possible states of a command template in the HEAppE system.  
/// </summary>
/// <remarks>This enumeration supports bitwise combination of its values. It is used to indicate the current
/// lifecycle state of a command template, such as whether it is pending initialization, active, inactive, or expired.
/// The <see cref="Unknown"/> value is typically used as a default or when the actual state cannot be
/// determined.</remarks>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ECommandTemplateStateResponse
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
    Inactive = 4,

    /// <summary>
    /// Indicates that the item or entity has expired and is no longer valid for use.
    /// </summary>
    Expired = 8
}