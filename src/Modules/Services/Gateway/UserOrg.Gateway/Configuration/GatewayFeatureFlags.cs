namespace UserOrg.Gateway.Configuration;

/// <summary>
/// Gateway feature flags that default on: an absent or blank <c>FeatureManagement:&lt;flag&gt;</c> value means on,
/// <c>true</c> or <c>false</c> is taken as written, and any other value stops the gateway at startup.
/// <para>
/// <c>V1Api</c> stays on <c>GetValue(..., true)</c> because the Core API reads the same key with that lenient parse.
/// </para>
/// </summary>
internal static class GatewayFeatureFlags
{
    /// <summary>Reads <c>FeatureManagement:&lt;flag&gt;</c>; on when absent or blank.</summary>
    /// <exception cref="InvalidOperationException">The value is neither blank nor a boolean.</exception>
    internal static bool IsEnabledByDefault(IConfiguration configuration, string flag)
    {
        var key = $"FeatureManagement:{flag}";
        return ParseDefaultOn(configuration[key], key);
    }

    /// <summary>Parses a default-on flag value; the error names <paramref name="key"/> and never the value.</summary>
    /// <exception cref="InvalidOperationException">The value is neither blank nor a boolean.</exception>
    internal static bool ParseDefaultOn(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return bool.TryParse(value.Trim(), out var enabled)
            ? enabled
            : throw new InvalidOperationException($"{key} must be 'true' or 'false'.");
    }
}