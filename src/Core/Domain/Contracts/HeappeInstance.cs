using UserOrg.Shared;
using UserOrg.Shared.Extensions;

namespace UserOrganizationManagement.Domain.Contracts;

/// <summary>
/// Represents a specific HEAppE (High-End Application Execution Environment) instance, including its service endpoint
/// and version information.
/// </summary>
/// <param name="Identifier">The identifier of the instance in the NexusApi registry; every HEAppE call is routed by it.</param>
/// <param name="Version">The version of the HEAppE instance. Cannot be null.</param>
/// <param name="Uri">The direct URL of the HEAppE instance, kept for logs only; <c>null</c> when none is stored.</param>
public sealed record HeappeInstance
(
    HeappeIdentifier Identifier,
    Version Version,
    Uri? Uri
);

/// <summary>
/// Provides extension methods for creating and configuring HEAppE service instances from location configuration values.
/// </summary>
public static class HeappeInstanceExtensions
{
    /// <summary>
    /// Creates a HeappeInstance from a collection of location values by extracting HEAPPE_IDENTIFIER, HEAPPE_VERSION
    /// and the HEAPPE_URL kept for logs.
    /// </summary>
    /// <param name="locationValues">The collection of key-value pairs representing location configuration values.</param>
    /// <returns>A new HeappeInstance, or <c>null</c> when the values carry no valid HEAPPE_IDENTIFIER (there is no URL fallback: a call cannot be routed without it).</returns>
    public static HeappeInstance? ToHeappeInstance(this IList<KeyValuePair<string, string>> locationValues)
    {
        var heappeIdentifier = locationValues
            .Where(v => v.Key.Equals(ApplicationConstants.HeappeIdentifierKey, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Value)
            .FirstOrDefault();
        if (!HeappeIdentifier.TryParse(heappeIdentifier, out var identifier))
        {
            return null;
        }

        var heappeUrl = locationValues
            .Where(v => v.Key.Equals(ApplicationConstants.HeappeUriKey, StringComparison.OrdinalIgnoreCase))
            .Select(v => Uri.TryCreate(v.Value, UriKind.Absolute, out var url) ? url.ToNormalizeUrl() : null)
            .FirstOrDefault();

        var versionText = locationValues
            .Where(v => v.Key.Equals(ApplicationConstants.HeappeVersionKey, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Value)
            .FirstOrDefault() ?? "0.0.0";

        if (!versionText.TryExtractVersion(out var heappeVersion))
        {
            heappeVersion = new Version(0, 0, 0);
        }

        return new HeappeInstance(identifier, heappeVersion!, heappeUrl);
    }
}