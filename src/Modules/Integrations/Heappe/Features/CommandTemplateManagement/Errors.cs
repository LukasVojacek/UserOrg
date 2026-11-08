using Heappe.Persistence.Enums;

using Lukdrasil.Error;

namespace Heappe.Features.CommandTemplateManagement;

internal static partial class Errors
{
    internal static Error FailedToRetrieveCommandTemplatesForProject(string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to retrieve command templates for project '{0}' and resource '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, resourceName);

    internal static Error FailedToRetrieveCommandTemplateForProject(Guid commandTemplateId, string projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to retrieve command template 'Id: {0}' for project '{1}' and resource '{2}' .")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(commandTemplateId, projectShortName, resourceName);

    internal static Error CommandTemplatePartitionsInconsistentState(Guid commandTemplateId, string projectShortName, string resourceName, ECommandTemplateState requiredState, IList<string> actualStates) => new ErrorBuilder()
        .WithDescription("Cannot update command template 'Id: {0}' for project '{1}' / resource '{2}'. All partitions must be in state '{3}' but found: [{4}].")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(commandTemplateId, projectShortName, resourceName, requiredState, string.Join(", ", actualStates));

    internal static Error FailedToUpdateCommandTemplateState(Guid commandTemplateId, string projectShortName, string resourceName, ECommandTemplateState targetState) => new ErrorBuilder()
        .WithDescription("Database update returned 0 rows for command template 'Id: {0}' in project '{1}' / resource '{2}'. Expected transition to state '{3}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(commandTemplateId, projectShortName, resourceName, targetState);
}