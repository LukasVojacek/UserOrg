using Lukdrasil.Error;

namespace Userorg.Adapter.Waldur.Base.Heappe;

/// <summary>
/// Provides factory methods for creating error instances related to HEAppE resource initialization and configuration
/// issues.
/// </summary>
/// <remarks>This static class centralizes error creation for scenarios where required HEAppE resource data,
/// endpoints, or configuration mappings are missing or incomplete. The errors produced are intended for use in
/// diagnostic and validation workflows within the HEAppE integration module.</remarks>
internal static partial class Errors
{
    internal static Error MissingResourceData(string providerId) => new ErrorBuilder()
        .WithDescription("HEAppE initialization skipped: Missing or incomplete resource data for provider '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(providerId);

    internal static Error MissingLocationEndpoint(string resourceName, string providerId) => new ErrorBuilder()
        .WithDescription("HEAppE initialization skipped: Cannot determine location endpoint for resource '{0}' and provider '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(resourceName, providerId);

    internal static Error MissingLocalBasePath(string resourceName, string providerId) => new ErrorBuilder()
        .WithDescription("HEAppE initialization skipped: Local base path not configured for resource '{0}' and provider '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(resourceName, providerId);

    internal static Error HeappeInstanceNotConfigured(string resourceName, string providerId) => new ErrorBuilder()
        .WithDescription("HEAppE initialization skipped: HEAppE instance mapping missing or incomplete for resource '{0}' and provider '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(resourceName, providerId);

    internal static Error UnspecifiedAllocationUnitType(string resourceName, string providerId, string? allocationUnitType) => new ErrorBuilder()
        .WithDescription("HEAppE initialization skipped: Unrecognized or unspecified allocation unit type '{2}' for resource '{0}' and provider '{1}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(resourceName, providerId, allocationUnitType ?? "null");

    internal static Error FailedToRetrieveAuthTypes => new ErrorBuilder()
        .WithDescriptionText("HEAppE initialization skipped: Failed to retrieve authentication types.")
        .WithSeverity(ErrorSeverity.Error)
        .Build();

    internal static Error PreferredAuthTypeNotFound(string preferredAuthType) => new ErrorBuilder()
        .WithDescription("HEAppE initialization skipped: Preferred authentication type '{0}' not found.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(preferredAuthType);
}