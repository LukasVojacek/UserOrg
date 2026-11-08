using Microsoft.Extensions.Options;

namespace UserOrg.Gateway.Configuration;

/// <summary>
/// Validates PublicEndpointsOptions configuration.
/// </summary>
internal sealed class PublicEndpointsOptionsValidator : IValidateOptions<PublicEndpointsOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicEndpointsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Paths is null)
            return ValidateOptionsResult.Fail("Paths collection cannot be null.");

        foreach (var path in options.Paths)
        {
            if (string.IsNullOrWhiteSpace(path.Pattern))
                return ValidateOptionsResult.Fail("Pattern cannot be empty.");

            if (!Enum.IsDefined(path.MatchType))
                return ValidateOptionsResult.Fail($"Invalid MatchType: {path.MatchType}");
        }

        return ValidateOptionsResult.Success;
    }
}