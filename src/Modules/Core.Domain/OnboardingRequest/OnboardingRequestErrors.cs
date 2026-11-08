using Lukdrasil.Error;

namespace Core.Domain.OnboardingRequest;

internal static class OnboardingRequestErrors
{
    internal static Error InvalidState(OnboardingRequestState from, OnboardingRequestState to) =>
        new ErrorBuilder()
            .WithCode(OnboardingRequestErrorCodes.InvalidState)
            .WithDescription("Cannot transition onboarding request from '{0}' to '{1}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(from.Name, to.Name);

    internal static Error AlreadyDeleted() =>
        new ErrorBuilder()
            .WithCode(OnboardingRequestErrorCodes.AlreadyDeleted)
            .WithDescription("Onboarding request is already deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error NotDeleted() =>
        new ErrorBuilder()
            .WithCode(OnboardingRequestErrorCodes.NotDeleted)
            .WithDescription("Onboarding request is not deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error AlreadyActiveMember(ProjectShortName projectShortName) =>
        new ErrorBuilder()
            .WithCode(OnboardingRequestErrorCodes.AlreadyActiveMember)
            .WithDescription("You are already an active member of project '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(projectShortName.Value);

    internal static Error DuplicatePendingRequest(ProjectShortName projectShortName) =>
        new ErrorBuilder()
            .WithCode(OnboardingRequestErrorCodes.DuplicatePendingRequest)
            .WithDescription("You already have a pending onboarding request for project '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(projectShortName.Value);

    internal static Error NotFound() =>
        new ErrorBuilder()
            .WithCode(OnboardingRequestErrorCodes.NotFound)
            .WithDescription("Onboarding request not found.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();
}