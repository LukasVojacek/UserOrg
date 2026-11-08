using System;

namespace UserOrg.Gateway.Startup;

internal sealed class CyclopsOptions
{
    public const string ConfigurationSection = "Cyclops";
    // Base URL for Cyclops (no trailing slash)
    public required Uri BaseUrl { get; set; }

    // Relative path for lexis  endpoint (leading slash expected)
    public required Uri TimeUsageRelativeUrl { get; set; }

    // Relative path for total usage endpoint (leading slash expected)
    public required Uri TotalUsageRelativeUrl { get; set; }
}