using Lukdrasil.Error;

namespace Core.Features.UserInfo.UseCases;

internal static class CallerProvisioningErrors
{
    internal const string NotProvisionableCode = "caller.not_provisioned";

    internal static Error NotProvisionable() =>
        new ErrorBuilder()
            .WithCode(NotProvisionableCode)
            .WithDescription("The token lacks the email or preferred_username claim needed to provision the caller.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error Deactivated() =>
        new ErrorBuilder()
            .WithCode(UserErrorCodes.Deactivated)
            .WithDescription("The authenticated caller has been deactivated.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();
}