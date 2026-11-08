namespace UserOrg.Gateway.Features.ServiceValidation;

/// <summary>
/// Validates service-to-service caller identity for configured targets.
/// </summary>
internal interface IServiceCallerValidator
{
    /// <summary>
    /// Checks if the target service is configured for S2S validation.
    /// </summary>
    /// <param name="targetService">The target service name (e.g., "forge-api")</param>
    /// <returns>True if validation is required, false if no validation configured</returns>
    bool IsTargetConfigured(string targetService);

    /// <summary>
    /// Validates the calling service identity based on IP and X-Forwarded-Host header.
    /// </summary>
    /// <param name="context">The HTTP context of the incoming request</param>
    /// <param name="targetService">The target service being called (e.g., "forge-api")</param>
    /// <returns>
    /// The validated caller service name if validation passed,
    /// null if validation failed or target is not configured for validation
    /// </returns>
    string? ValidateCaller(HttpContext context, string targetService);
}