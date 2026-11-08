namespace UserOrg.Shared.FeatureManagement;

/// <summary>
/// Names of the feature flags shared across assembly boundaries.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Feature flag name for the legacy (v1) Core API surface.
    /// <para>
    /// Configuration key: <c>FeatureManagement:V1Api</c>. Default: <c>true</c> (absent = enabled), so an
    /// existing deployment that never heard of the flag keeps serving v1. <c>Program.cs</c> seeds the
    /// default into configuration at startup when the key is absent, because
    /// <c>IFeatureManager.IsEnabledAsync</c> reports an undefined flag as disabled and would otherwise
    /// turn every v1 route into a 404.
    /// </para>
    /// <para>
    /// Consumed in two places, mirroring <see cref="V2Api"/>:
    /// <list type="bullet">
    ///   <item>
    ///     <strong>Startup (synchronous):</strong> Read in <c>Program.cs</c> at composition time and gates
    ///     <c>MapV1CoreEndpoints()</c>. The v1 service registrations in <c>AddV1Core</c> stay unconditional:
    ///     the JWT pipeline resolves <c>IUserOrgUnitOfWork</c> and <c>IProviderIdResolver</c> per request, and
    ///     the Heappe adapter injects <c>IHeappeIntegrationService</c>, none of which is a v1 endpoint concern.
    ///     The flag also selects the published documents through <c>ApiDocuments.Published</c>: the legacy
    ///     User and Administration documents are registered and offered in Scalar only while it is on, and
    ///     the Service document of the ungated legacy endpoints is published always. The gateway reads the
    ///     same key once at startup to pick the documents it fetches and the document URLs it keeps public,
    ///     so both hosts must carry the same value.
    ///   </item>
    ///   <item>
    ///     <strong>Runtime (per-request):</strong> Applied via <c>EnabledWhen</c> on the v1 route groups and
    ///     checked by <c>FeatureFlagMiddleware</c>, so flipping the flag off through a config reload returns
    ///     404 without a restart. Flipping it on at runtime has no effect if it was off at startup.
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public const string V1Api = "V1Api";

    /// <summary>
    /// Feature flag name for the modular (v2) Core API surface.
    /// <para>
    /// Configuration key: <c>FeatureManagement:V2Api</c>. Default: <c>false</c> (absent = disabled).
    /// </para>
    /// <para>
    /// Consumed in two places with different semantics:
    /// <list type="bullet">
    ///   <item>
    ///     <strong>Startup (synchronous):</strong> Read in <c>Program.cs</c> at container composition time.
    ///     Determines whether v2 services (<c>AddCore</c>, <c>AddOutboxPersistence</c>, <c>AddResourceRequestsPersistence</c>,
    ///     <c>AddWorkflowEngine</c>) are registered and whether <c>MapV2CoreEndpoints()</c> runs. This is a one-way
    ///     decision: routes are only wired if the flag was on at startup.
    ///   </item>
    ///   <item>
    ///     <strong>Runtime (per-request):</strong> Applied via <c>EnabledWhen</c> on the <c>api/v2</c> route group,
    ///     checked per request by <c>FeatureFlagMiddleware</c>. Acts as a one-way kill-switch: if the flag is flipped
    ///     off at runtime via config reload, the routes return 404 without restarting. Flipping it on at runtime has
    ///     no effect because the routes were never mapped if it was off at startup.
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public const string V2Api = "V2Api";

    /// <summary>
    /// Feature flag name for the HEAppE per-instance token exchange.
    /// <para>
    /// Configuration key: <c>FeatureManagement:TokenExchangeEnabled</c>. Default: <c>false</c> (absent = disabled).
    /// Read once at startup (plain true/false only); a change takes effect after a restart.
    /// </para>
    /// </summary>
    public const string TokenExchangeEnabled = "TokenExchangeEnabled";

    /// <summary>
    /// Feature flag name for the strict audience check of the gateway user Bearer scheme (ADR 0121).
    /// <para>
    /// Configuration key: <c>FeatureManagement:StrictJwtAudience</c>. Default: <c>true</c> (absent or blank = enabled).
    /// When enabled, a user token is accepted only when its <c>aud</c> is non-empty and every value is
    /// <c>Idp:Audience</c> or one of <c>Idp:AdditionalAudiences</c>; when disabled, one matching audience is enough.
    /// A value that is neither blank nor a boolean stops the gateway at startup.
    /// Read once at startup; a change takes effect after a restart.
    /// Applies only to the gateway user Bearer scheme.
    /// </para>
    /// </summary>
    public const string StrictJwtAudience = "StrictJwtAudience";
}