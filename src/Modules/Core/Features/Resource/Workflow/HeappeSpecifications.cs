using Core.Features.Resource.CreateResourceOnLocation;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Exceptions;

namespace Core.Features.Resource.Workflow;

/// <summary>Reads the HEAppE routing values from the specifications of a location assignment.</summary>
internal static class HeappeSpecifications
{
    /// <summary>
    /// The HEAppE instance identifier every HEAppE call is routed by.
    /// </summary>
    /// <exception cref="PermanentException">The assignment carries no valid <c>HEAPPE_IDENTIFIER</c>; retrying cannot fix it.</exception>
    internal static HeappeIdentifier RequireIdentifier(this CreateResourceOnLocationEndpoint.LocationAssignment location) =>
        HeappeIdentifier.TryParse(SpecificationValue(location, ApplicationConstants.HeappeIdentifierKey), out var identifier)
            ? identifier
            : throw new PermanentException(
                "HEAPPE_IDENTIFIER_INVALID",
                $"Location '{location.LocationName}' carries no valid {ApplicationConstants.HeappeIdentifierKey} specification.");

    /// <summary>The direct HEAppE URL of the assignment, kept for logs only; <c>null</c> when absent or not absolute.</summary>
    internal static Uri? UriOrNull(this CreateResourceOnLocationEndpoint.LocationAssignment location) =>
        Uri.TryCreate(SpecificationValue(location, ApplicationConstants.HeappeUriKey), UriKind.Absolute, out var uri)
            ? uri.ToNormalizeUrl()
            : null;

    private static string? SpecificationValue(CreateResourceOnLocationEndpoint.LocationAssignment location, string name) =>
        location.Specifications?.FirstOrDefault(x => x.Name == name)?.Value;
}