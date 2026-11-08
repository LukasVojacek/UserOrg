using Microsoft.Extensions.Options;

namespace Heappe.Features.Connection;

/// <summary>
/// Validates <see cref="TokenExchangeOptions"/>: with the token exchange flag on, the client, its secret and
/// an absolute http or https authority are required.
/// </summary>
/// <param name="tokenExchangeEnabled">Indicates whether the token exchange is enabled.</param>
internal sealed class TokenExchangeOptionsValidator(bool tokenExchangeEnabled) : IValidateOptions<TokenExchangeOptions>
{
    private static TimeSpan MinBreakerDuration => TimeSpan.FromMilliseconds(500);

    public ValidateOptionsResult Validate(string? name, TokenExchangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!tokenExchangeEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{TokenExchangeOptions.SectionName}: 'ClientId' is required with the token exchange flag on.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add($"{TokenExchangeOptions.SectionName}: 'ClientSecret' is required with the token exchange flag on.");
        }

        if (!IsValidAuthorityBaseUrl(options.Authority))
        {
            failures.Add($"{TokenExchangeOptions.SectionName}: 'AuthorityBaseUrl' is required and must be an absolute http or https realm URI without userinfo, query or fragment, for example 'https://keycloak/realms/realm'.");
        }
        
        if (!IsValidTokenEndpointPath(options.TokenEndpointPath))
        {
            failures.Add($"{TokenExchangeOptions.SectionName}: 'TokenEndpointPath' is required and must be a relative path starting with '/' without query or fragment, for example '/protocol/openid-connect/token'.");
        }

        //Validate resilience options for TokenChange
        ValidateResilience(options.Resilience, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateResilience(TokenExchangeResilienceOptions? resilience, List<string> failures)
    {
        const string prefix = $"{TokenExchangeOptions.SectionName}:{nameof(TokenExchangeOptions.Resilience)}";

        if (resilience.AttemptTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{prefix}: 'AttemptTimeout' must be greater than zero.");
        }

        if (resilience.TotalRequestTimeout < resilience.AttemptTimeout)
        {
            failures.Add($"{prefix}: 'TotalRequestTimeout' must not be shorter than 'AttemptTimeout'.");
        }

        if (resilience.MinimumThroughput < 2)
        {
            failures.Add($"{prefix}: 'MinimumThroughput' must be at least 2.");
        }

        if (resilience.SamplingDuration < MinBreakerDuration)
        {
            failures.Add($"{prefix}: 'SamplingDuration' must be at least {MinBreakerDuration:hh\\:mm\\:ss}.");
        }

        if (resilience.BreakDuration < MinBreakerDuration)
        {
            failures.Add($"{prefix}: 'BreakDuration' must be at least {MinBreakerDuration:hh\\:mm\\:ss}.");
        }
    }

    private static bool IsValidTokenEndpointPath(Uri? relative) =>
        relative is { IsAbsoluteUri: false }
        && relative.OriginalString.StartsWith('/')
        && !relative.OriginalString.StartsWith("//", StringComparison.Ordinal)
        && relative.OriginalString.AsSpan().IndexOfAny('?', '#') < 0;

    private static bool IsValidAuthorityBaseUrl(Uri? authority) =>
        authority is { IsAbsoluteUri: true }
        && (authority.Scheme == Uri.UriSchemeHttp || authority.Scheme == Uri.UriSchemeHttps)
        && authority.UserInfo.Length == 0
        && authority.Query.Length == 0
        && authority.Fragment.Length == 0
        && authority.AbsolutePath.EndsWith('/');
}
