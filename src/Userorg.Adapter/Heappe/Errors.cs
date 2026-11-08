using Heappe.Contracts;

using Lukdrasil.Error;

using UserOrg.Shared;

using UserOrganizationManagement.Domain.ValueObjects;

namespace Userorg.Adapter.Heappe;

internal static class Errors
{
    internal static Error FailedToRetrieveProjectAllocationWithResources(ProjectShortName projectShortName, string resourceName) => new ErrorBuilder()
        .WithDescription("Failed to retrieve project with resources for project '{0}' and resource '{1}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(projectShortName, resourceName);

    internal static Error FailedToRetrieveProjectsForUserWithHeappeIdentifier(string idpUserId, string heappeIdentifier) => new ErrorBuilder()
        .WithDescription("Failed to retrieve projects for user '{0}' and HEAppE identifier '{1}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(idpUserId, heappeIdentifier);

    internal static Error UnexpectedErrorRetrievingProjectAllocations(string idpUserId, string heappeIdentifier, string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving project for user '{0}' with HEAppE identifier '{1}' ('{2}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(idpUserId, heappeIdentifier, exceptionMessage);

    internal static Error UnexpectedErrorRetrievingProjectResourceAllocation(string projectShortName, string resourceName, string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving project for project '{0}' with resource '{1}' ('{2})")
        .WithSeverity(ErrorSeverity.Error)
        .Build(projectShortName, resourceName, exceptionMessage);

    internal static Error HeappeInstanceUnreachable(HeappeIdentifier identifier) => new ErrorBuilder()
        .WithDescription("HEAppE instance '{0}' did not answer its health probe; skipping it as unavailable.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(identifier);

    internal static Error HeappeAllocationMissing(string resourceName, string clusterNames) => new ErrorBuilder()
        .WithCode(HeappeFailureCause.AllocationMissing)
        .WithDescription("HEAppE cluster(s) [{1}] carry no active allocation for resource '{0}'; catalog treated as incomplete and not cached.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(resourceName, clusterNames);

    internal static Error ProviderNotFoundByName(string providerName) => new ErrorBuilder()
        .WithDescription("No provider found with name '{0}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(providerName);

    internal static Error AmbiguousProviderName(string providerName) => new ErrorBuilder()
        .WithDescription("More than one provider matches name '{0}'; provider names are not unique.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(providerName);

    internal static Error UnexpectedErrorResolvingProviderName(string providerName, string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while resolving provider by name '{0}' ('{1}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(providerName, exceptionMessage);

    internal static Error UnexpectedErrorRetrievingLocationIdsForProvider(Guid providerId, string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving location ids for provider '{0}' ('{1}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(providerId, exceptionMessage);

    internal static Error UnexpectedErrorRetrievingProviderLocations(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while retrieving provider locations ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);

    internal static Error UserNotFoundByIdentityProviderId(string identityProviderUserId) => new ErrorBuilder()
        .WithDescription("No user found for identity-provider user id '{0}'.")
        .WithSeverity(ErrorSeverity.Warning)
        .Build(identityProviderUserId);

    internal static Error UnexpectedErrorResolvingUser(string identityProviderUserId, string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while resolving user '{0}' ('{1}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(identityProviderUserId, exceptionMessage);

    internal static Error UnexpectedErrorResolvingProjectResourceNames(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while resolving project resource names ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);

    internal static Error UnexpectedErrorResolvingProjectResourceIds(string exceptionMessage) => new ErrorBuilder()
        .WithDescription("Unexpected error occurred while resolving project resource ids ('{0}')")
        .WithSeverity(ErrorSeverity.Error)
        .Build(exceptionMessage);
}