namespace UserOrg.Shared.FeatureManagement;

/// <summary>
/// Metadata that marks an endpoint as enabled only when a specific feature flag is enabled.
/// </summary>
/// <remarks>
/// This metadata is used by the <see cref="FeatureFlagMiddleware"/> to determine if an endpoint
/// should be blocked (return 404) based on feature flag state. The endpoint is only reachable when
/// the associated feature flag is enabled; when the flag is disabled (OFF) the endpoint returns 404.
/// When used with the <see cref="EndpointExtensions.EnabledWhen"/> extension method, it provides a
/// clean, fluent API for marking endpoints.
/// </remarks>
public class EnabledByFeatureFlagMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnabledByFeatureFlagMetadata"/> class.
    /// </summary>
    /// <param name="featureFlagName">The name of the feature flag that enables this endpoint.</param>
    public EnabledByFeatureFlagMetadata(string featureFlagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureFlagName);
        FeatureFlagName = featureFlagName;
    }

    /// <summary>
    /// Gets the name of the feature flag that enables this endpoint.
    /// </summary>
    public string FeatureFlagName { get; }
}