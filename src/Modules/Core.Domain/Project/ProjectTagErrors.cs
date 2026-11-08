using Lukdrasil.Error;

namespace Core.Domain.Project;

internal static class ProjectTagErrors
{
    internal static Error TagNotFound(TagName name) =>
        new ErrorBuilder()
            .WithCode(ProjectTagErrorCodes.TagNotFound)
            .WithDescription("Tag '{0}' was not found.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);

    internal static Error TagInUse(TagName name) =>
        new ErrorBuilder()
            .WithCode(ProjectTagErrorCodes.TagInUse)
            .WithDescription("Tag '{0}' is assigned to one or more projects and cannot be deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);

    internal static Error AlreadyDeleted(TagName name) =>
        new ErrorBuilder()
            .WithCode(ProjectTagErrorCodes.AlreadyDeleted)
            .WithDescription("Tag '{0}' is already deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);
}