using Lukdrasil.Error;

namespace Core.Domain.Request;

internal static class RequestErrors
{
    internal static Error InvalidStateTransition(RequestState from, RequestState to) =>
        new ErrorBuilder()
            .WithCode(RequestErrorCodes.InvalidStateTransition)
            .WithDescription("Cannot transition request from '{0}' to '{1}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(from.GetDescription(), to.GetDescription());

    internal static Error AlreadyDeleted() =>
        new ErrorBuilder()
            .WithCode(RequestErrorCodes.AlreadyDeleted)
            .WithDescription("Request is already deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error NotDeleted() =>
        new ErrorBuilder()
            .WithCode(RequestErrorCodes.NotDeleted)
            .WithDescription("Request is not deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error AlreadyActiveMember(ProjectShortName projectShortName) =>
        new ErrorBuilder()
            .WithCode(RequestErrorCodes.AlreadyActiveMember)
            .WithDescription("You are already an active member of project '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(projectShortName.Value);

    internal static Error DuplicatePendingRequest(ProjectShortName projectShortName) =>
        new ErrorBuilder()
            .WithCode(RequestErrorCodes.DuplicatePendingRequest)
            .WithDescription("You already have a pending request for project '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(projectShortName.Value);

    internal static Error UnsupportedType(RequestType type) =>
        new ErrorBuilder()
            .WithCode(RequestErrorCodes.UnsupportedType)
            .WithDescription("Request type '{0}' is not supported on this endpoint; only self-onboarding requests are accepted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(type.Value.ToString());
}