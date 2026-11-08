using System.Collections.Immutable;

namespace UserOrg.Shared.OpenApi;

/// <summary>
/// The names of the OpenAPI documents the Core API publishes, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Core registers one document per name (<c>AddOpenApi</c> in <c>V1CoreExtensions</c> and
/// <c>V2CoreExtensions</c>) and serves each at <c>/api/{name}/openapi.json</c>. Two consumers have to
/// agree with that registration exactly: the Scalar UI, which resolves every name it is given, and the
/// gateway's route-existence oracle (ADR-0106), which reads the documents to decide whether a path
/// describes a live route.
/// </para>
/// <para>
/// The oracle is why this list is shared rather than copied: a document Core registers and the gateway
/// does not read is never fetched, and every route described only by that document is answered
/// <c>404 route.not_found</c> although it works. A name added here reaches both sides at once; a name
/// added to an <c>AddOpenApi</c> call alone is caught by the guard test over Core's registrations.
/// </para>
/// </remarks>
public static class ApiDocuments
{
    /// <summary>The legacy (v1) user-facing document.</summary>
    public const string User = "User";

    /// <summary>The legacy (v1) administration document.</summary>
    public const string Administration = "Administration";

    /// <summary>The v2 document for the user audience.</summary>
    public const string V2User = "v2-User";

    /// <summary>The v2 document for the provider-administrator audience.</summary>
    public const string V2ProviderAdmin = "v2-ProviderAdmin";

    /// <summary>The v2 document for the administrator audience.</summary>
    public const string V2Admin = "v2-Admin";

    /// <summary>
    /// The always-published document of the ungated legacy endpoints: RemI, <c>api/version</c>,
    /// <c>api/DataStaging</c> and <c>api/Heappe</c>.
    /// </summary>
    public const string Service = "Service";

    /// <summary>
    /// The published document names: <see cref="User"/> and <see cref="Administration"/> only while the
    /// V1Api flag is on, <see cref="Service"/> and the v2 documents always.
    /// </summary>
    public static ImmutableArray<string> Published(bool isV1ApiEnabled) =>
        isV1ApiEnabled ? [User, Administration, Service, V2User, V2ProviderAdmin, V2Admin] : [Service, V2User, V2ProviderAdmin, V2Admin];
}