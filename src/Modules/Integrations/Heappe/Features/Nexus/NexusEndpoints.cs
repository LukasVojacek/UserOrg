using UserOrg.Shared;

namespace Heappe.Features.Nexus;

/// <summary>How a HEAppE instance is addressed behind the NexusApi proxy at <c>TokenExchange:Nexus:BaseUrl</c>.</summary>
internal static class NexusEndpoints
{
    /// <summary>The proxy base of one HEAppE instance: <c>{baseUrl}/api/heappe/{identifier}/</c>, with or without a trailing slash on <paramref name="baseUrl"/>.</summary>
    public static Uri ProxyBaseFor(Uri baseUrl, HeappeIdentifier identifier) =>
        new($"{baseUrl.AbsoluteUri.TrimEnd('/')}/api/heappe/{identifier.Value}/");
}