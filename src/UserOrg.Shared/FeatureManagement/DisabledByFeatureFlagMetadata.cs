namespace UserOrg.Shared.FeatureManagement;

/// <summary>
/// Metadata that marks an endpoint as disabled when a specific feature flag is enabled.
/// </summary>
/// <remarks>
/// This metadata is used by the <see cref="FeatureFlagMiddleware"/> to determine if an endpoint
/// should be blocked (return 404) based on feature flag state. When used with the 
/// <see cref="EndpointExtensions.DisabledWhen"/> extension method, it provides a clean,
/// fluent API for marking endpoints.
/// </remarks>
public class DisabledByFeatureFlagMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisabledByFeatureFlagMetadata"/> class.
    /// </summary>
    /// <param name="featureFlagName">The name of the feature flag that disables this endpoint.</param>
    public DisabledByFeatureFlagMetadata(string featureFlagName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureFlagName);
        FeatureFlagName = featureFlagName;
    }

    /// <summary>
    /// Gets the name of the feature flag that disables this endpoint.
    /// </summary>
    public string FeatureFlagName { get; }
}