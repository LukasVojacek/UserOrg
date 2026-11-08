using Microsoft.Extensions.Options;

namespace Waldur.Sync.Configuration;

/// <summary>
/// Validates <see cref="WaldurSyncOptions"/> at startup. Only <b>enabled</b> instances are checked;
/// an enabled instance missing auth, identity or endpoints fails fast (fail loudly, not silently).
/// </summary>
internal sealed class WaldurSyncOptionsValidator : IValidateOptions<WaldurSyncOptions>
{
    public ValidateOptionsResult Validate(string? name, WaldurSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        var index = 0;
        foreach (var instance in options.Instances)
        {
            if (instance.Enabled)
            {
                ValidateInstance(instance, index, failures);
            }

            index++;
        }

        var names = options.EnabledInstances
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            failures.Add("Enabled Waldur instances must have unique Name values.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateInstance(WaldurInstanceOptions instance, int index, List<string> failures)
    {
        var prefix = $"Waldur:Instances:{index}";

        void Require(bool present, string field)
        {
            if (!present)
            {
                failures.Add($"{prefix} ({instance.Name}): '{field}' is required for an enabled instance.");
            }
        }

        Require(!string.IsNullOrWhiteSpace(instance.Name), nameof(instance.Name));
        Require(instance.BaseUrl is { IsAbsoluteUri: true }, nameof(instance.BaseUrl));

        Require(instance.CustomersEndpoint is not null, nameof(instance.CustomersEndpoint));
        Require(instance.ProjectsEndpoint is not null, nameof(instance.ProjectsEndpoint));
        Require(instance.MarketplacePublicOfferingsEndpoint is not null, nameof(instance.MarketplacePublicOfferingsEndpoint));
        Require(instance.MarketplaceResourcesEndpoint is not null, nameof(instance.MarketplaceResourcesEndpoint));

        void RequireRelative(Uri? endpoint, string field)
        {
            if (endpoint is { IsAbsoluteUri: true })
            {
                failures.Add($"{prefix} ({instance.Name}): '{field}' must be relative to BaseUrl, not an absolute URI.");
            }
        }

        RequireRelative(instance.CustomersEndpoint, nameof(instance.CustomersEndpoint));
        RequireRelative(instance.ProjectsEndpoint, nameof(instance.ProjectsEndpoint));
        RequireRelative(instance.MarketplacePublicOfferingsEndpoint, nameof(instance.MarketplacePublicOfferingsEndpoint));
        RequireRelative(instance.MarketplaceResourcesEndpoint, nameof(instance.MarketplaceResourcesEndpoint));

        Require(!string.IsNullOrWhiteSpace(instance.AuthorizationHeader), nameof(instance.AuthorizationHeader));
        Require(!string.IsNullOrWhiteSpace(instance.EndpointApiKey), nameof(instance.EndpointApiKey));

        Require(!string.IsNullOrWhiteSpace(instance.Identity.ServiceName), "Identity.ServiceName");
        Require(!string.IsNullOrWhiteSpace(instance.Identity.ServiceAccountUserName), "Identity.ServiceAccountUserName");
        Require(!string.IsNullOrWhiteSpace(instance.Identity.WaldurPIEmail), "Identity.WaldurPIEmail");

        Require(instance.PageSizeLimit > 0, nameof(instance.PageSizeLimit));
    }
}