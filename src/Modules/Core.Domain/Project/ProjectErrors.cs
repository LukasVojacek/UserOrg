using Lukdrasil.Error;

namespace Core.Domain.Project;

internal static class ProjectErrors
{
    internal static Error ProjectIsReadOnly(ProjectShortName shortName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.ReadOnly)
            .WithDescription("Project '{0}' is read-only and cannot be modified.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(shortName.Value);

    internal static Error ProjectIsDeleted(ProjectShortName shortName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.Deleted)
            .WithDescription("Project '{0}' is deleted and cannot be modified.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(shortName.Value);

    internal static Error ProjectIsExpired(ProjectShortName shortName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.Expired)
            .WithDescription("Project '{0}' is expired and cannot be modified.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(shortName.Value);

    internal static Error CannotExpireDeletedProject(ProjectShortName shortName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.CannotExpireDeleted)
            .WithDescription("Project '{0}' is deleted and cannot be expired.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(shortName.Value);

    internal static Error EndDateBeforeStartDate() =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.EndDateBeforeStartDate)
            .WithDescription("Project end date cannot be earlier than its start date.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error UserAlreadyAssigned(UserName userName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.UserAlreadyAssigned)
            .WithDescription("User '{0}' is already assigned to the project.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(userName.Value);

    internal static Error UserNotAssigned(UserName userName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.UserNotAssigned)
            .WithDescription("User '{0}' is not assigned to the project.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(userName.Value);

    internal static Error CannotAssignEqualOrHigherRole() =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.CannotAssignEqualOrHigherRole)
            .WithDescription("You can only assign a role strictly below your own level and no lower than the project member baseline.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error DuplicateProjectOwner() =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.DuplicateProjectOwner)
            .WithDescription("The project already has an owner; a second owner cannot be assigned.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error OwnerCannotBeUnassigned() =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.OwnerCannotBeUnassigned)
            .WithDescription("The project owner cannot be unassigned from the project.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

    internal static Error ResourceAlreadyAssigned(ResourceName resourceName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.ResourceAlreadyAssigned)
            .WithDescription("Resource '{0}' is already assigned to the project.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(resourceName.Value);

    internal static Error ResourceNotAssigned(ResourceName resourceName) =>
        new ErrorBuilder()
            .WithCode(ProjectErrorCodes.ResourceNotAssigned)
            .WithDescription("Resource '{0}' is not assigned to the project.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(resourceName.Value);
}