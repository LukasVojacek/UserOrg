using Microsoft.Extensions.Options;

namespace Heappe.Features.Nexus;

/// <summary>
/// Validates <see cref="NexusOptions"/> values for Nexus API integration and service account requirements.
/// </summary>
/// <remarks>Validation enforces an absolute HTTP or HTTPS base URI without user info, query, or fragment, and
/// requires cache TTL to be between 00:00:01 and 01:00:00.</remarks>
/// <param name="tokenExchangeEnabled">Indicates whether token exchange is enabled and service account credentials are required during validation.</param>
internal sealed class NexusOptionsValidator(bool tokenExchangeEnabled) : IValidateOptions<NexusOptions>
{
    /// <summary>
    /// Gets the minimum time-to-live for map cache entries.
    /// </summary>
    private static TimeSpan MinMapCacheTtl => TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the maximum time-to-live for map cache entries.
    /// </summary>
    private static TimeSpan MaxMapCacheTtl => TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, NexusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];

        if (!IsValidBaseUrl(options.BaseUrl))
        {
            failures.Add($"{NexusOptions.SectionName}: 'BaseUrl' is required and must be an absolute http or https URI at the host root without userinfo, query or fragment.");
        }

        if (options.CacheMapTtl < MinMapCacheTtl || options.CacheMapTtl > MaxMapCacheTtl)
        {
            failures.Add($"{NexusOptions.SectionName}: 'CacheMapTtl' must be between {MinMapCacheTtl:hh\\:mm\\:ss} and {MaxMapCacheTtl:hh\\:mm\\:ss}.");
        }

        if (tokenExchangeEnabled)
        {
            if (string.IsNullOrWhiteSpace(options.ClientId))
            {
                failures.Add($"{NexusOptions.SectionName}: 'ClientId' for service account is required with the token exchange flag on.");
            }

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                failures.Add($"{NexusOptions.SectionName}: 'ClientSecret' for service account is required with the token exchange flag on.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidBaseUrl(Uri? baseUrl) =>
        baseUrl is { IsAbsoluteUri: true }
        && (baseUrl.Scheme == Uri.UriSchemeHttp || baseUrl.Scheme == Uri.UriSchemeHttps)
        && baseUrl.UserInfo.Length == 0
        && baseUrl.Query.Length == 0
        && baseUrl.Fragment.Length == 0;
}