using Lukdrasil.Error;

namespace Heappe.Features.CommandTemplatePermissions;

internal static partial class Errors
{
    internal static Error FailedToRetrieveProjectsForUserWithHeappeIdentifier(string idpUserId, string heappeIdentifier) => new ErrorBuilder()
        .WithDescription("Failed to retrieve projects for user '{0}' and HEAppE identifier '{1}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(idpUserId, heappeIdentifier);
}