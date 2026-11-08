using Lukdrasil.Error;

namespace Core.Domain.Provider;

internal static class ProviderErrors
{
    internal static Error AlreadyDeactivated(ProviderName name) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.AlreadyDeactivated)
            .WithDescription("Provider '{0}' is already deactivated.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);

    internal static Error NotDeactivated(ProviderName name) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.NotDeactivated)
            .WithDescription("Provider '{0}' is not deactivated.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);

    internal static Error NameAlreadyExists(ProviderName name) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.NameAlreadyExists)
            .WithDescription("A provider named '{0}' already exists.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);

    internal static Error LocationAlreadyExists(LocationName locationName, ProviderName providerName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.LocationAlreadyExists)
            .WithDescription("Location '{0}' already exists on provider '{1}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value, providerName.Value);

    internal static Error LocationNotFound(LocationName locationName, ProviderName providerName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.LocationNotFound)
            .WithDescription("Location '{0}' was not found on provider '{1}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value, providerName.Value);

    internal static Error LocationAlreadyDeleted(LocationName locationName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.LocationAlreadyDeleted)
            .WithDescription("Location '{0}' is already deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value);

    internal static Error AssignedLocationNotFound(LocationName locationName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.AssignedLocationNotFound)
            .WithDescription("The specified assigned location was not found on location '{0}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value);

    internal static Error AssignedLocationAlreadyDeleted(LocationName locationName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.AssignedLocationAlreadyDeleted)
            .WithDescription("The specified assigned location on '{0}' is already deleted.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value);

    internal static Error ProviderIsReadOnly(ProviderName name) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.ProviderReadOnly)
            .WithDescription("Provider '{0}' is in read-only mode and cannot be modified.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(name.Value);

    internal static Error LocationReadOnly(LocationName locationName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.LocationReadOnly)
            .WithDescription("Location '{0}' is in read-only mode and cannot be modified.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value);

    internal static Error AssignmentReadOnly(LocationName locationName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.AssignmentReadOnly)
            .WithDescription("The specified assignment on location '{0}' is in read-only mode and cannot be modified.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value);

    internal static Error ResourceAlreadyHasNonSharedLocation(ResourceName resourceName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.ResourceAlreadyHasNonSharedLocation)
            .WithDescription("Project resource '{0}' already has an assigned non-shared location.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(resourceName.Value);

    internal static Error ResourceNotUnderProvider(ResourceName resourceName, ProviderName providerName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.ResourceNotUnderProvider)
            .WithDescription("Project resource '{0}' does not belong to provider '{1}'.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(resourceName.Value, providerName.Value);

    internal static Error NonSharedLocationAlreadyHasResource(LocationName locationName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.NonSharedLocationAlreadyHasResource)
            .WithDescription("Non-shared location '{0}' already has an assigned resource.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(locationName.Value);

    internal static Error NoLocationAssignments(ResourceName resourceName) =>
        new ErrorBuilder()
            .WithCode(ProviderErrorCodes.NoLocationAssignments)
            .WithDescription("Project resource '{0}' must be assigned to at least one location.")
            .WithSeverity(ErrorSeverity.Error)
            .Build(resourceName.Value);
}