namespace Heappe.Features.CommandTemplateManagement.GetAll.Response;

/// <summary>
/// Specifies the source type of a command template response.
/// </summary>
/// <remarks>Use this enumeration to distinguish whether a command template response is defined by the system, by
/// the user, or is unknown. The default value is typically <see
/// cref="ECommandTemplateTypeResponse.Unknown"/>.</remarks>
internal enum ECommandTemplateTypeResponse
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
}