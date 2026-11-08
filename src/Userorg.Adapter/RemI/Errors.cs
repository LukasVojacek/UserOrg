using Lukdrasil.Error;

namespace Userorg.Adapter.RemI;

internal static class Errors
{
    internal static Error UnexpectedErrorRetrievingUsers(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving users ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);

    internal static Error UnexpectedErrorRetrievingProjects(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving projects ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);

    internal static Error UnexpectedErrorRetrievingUser(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving user ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);

    internal static Error UnexpectedErrorRetrievingUserSystemRoles(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving user system roles ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);
}