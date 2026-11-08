using Heappe.Features.CommandTemplateManagement.GetAll.Response;

namespace Heappe.Persistence.Enums;

/// <summary>
/// Provides extension methods for converting between persistence and API response command template type enumerations.
/// </summary>
internal static class ECommandTemplateTypeExtensions
{
    /// <summary>
    /// Convert persistence enum to API response enum.
    /// </summary>
    internal static ECommandTemplateTypeResponse ToResponseType(this ECommandTemplateType type)
    {
        return type switch
        {
            ECommandTemplateType.SystemDefined => ECommandTemplateTypeResponse.SystemDefined,
            ECommandTemplateType.UserDefined => ECommandTemplateTypeResponse.UserDefined,
            ECommandTemplateType.Generic => ECommandTemplateTypeResponse.UserDefined,
            _ => ECommandTemplateTypeResponse.Unknown
        };
    }
}