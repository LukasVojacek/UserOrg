using Heappe.Features.CommandTemplateManagement.GetAll.Response;

namespace Heappe.Persistence.Enums;

/// <summary>
/// Provides extension methods for converting command template state enumeration values to their corresponding API
/// response representations.
/// </summary>
internal static class ECommandTemplateStateExtensions
{

    /// <summary>
    /// Converts the specified command template state to its corresponding response type, optionally marking it as
    /// expired.    
    /// </summary>
    /// <remarks>If the provided state does not match a known value, the response type will be set to Unknown.
    /// The Expired flag is combined with the response type when isExpired is true.</remarks>
    /// <param name="state">The command template state to convert.</param>
    /// <param name="isExpired">true to indicate that the state is expired and should be marked as such in the response; otherwise, false. The
    /// default is false.</param>
    /// <returns>A value of type ECommandTemplateStateResponse that represents the response type corresponding to the specified
    /// state. If isExpired is true, the Expired flag is included.</returns>
    internal static ECommandTemplateStateResponse ToResponseType(this ECommandTemplateState state, bool isExpired = false)
    {
        var response = state switch
        {
            ECommandTemplateState.Pending => ECommandTemplateStateResponse.Pending,
            ECommandTemplateState.Active => ECommandTemplateStateResponse.Active,
            ECommandTemplateState.Inactive => ECommandTemplateStateResponse.Inactive,
            _ => ECommandTemplateStateResponse.Unknown,
        };

        if (isExpired)
        {
            response |= ECommandTemplateStateResponse.Expired;
        }

        return response;
    }
}