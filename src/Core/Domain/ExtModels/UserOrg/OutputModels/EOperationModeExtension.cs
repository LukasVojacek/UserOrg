using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels;

public static class EOperationModeExtension
{
    public static EOperationModeResponse ToResponse(this EOperationMode mode)
    {
        return mode switch
        {
            EOperationMode.ReadOnly => EOperationModeResponse.ReadOnly,
            EOperationMode.Editable => EOperationModeResponse.Editable,
            _ => EOperationModeResponse.Unspecified
        };
    }
}