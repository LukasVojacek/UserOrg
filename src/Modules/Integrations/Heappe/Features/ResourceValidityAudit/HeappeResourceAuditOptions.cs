using System.ComponentModel.DataAnnotations;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// Configuration for the resource validity audit (section <c>Heappe:ResourceAudit</c>). The on/off switch is the
/// <c>HeappeResourceAudit</c> feature flag (Microsoft.FeatureManagement), not an option here.
/// </summary>
internal sealed class HeappeResourceAuditOptions
{
    internal const string SectionName = "Heappe:ResourceAudit";

    /// <summary>MS Teams (Power Automate "Workflows") incoming webhook the findings card is POSTed to.</summary>
    public Uri? WebhookUrl { get; set; }

    /// <summary>Interval between scheduled runs. Validated at startup; defaults to 8 hours.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00", ErrorMessage = "Heappe:ResourceAudit:Interval must be between 1 minute and 7 days.")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(8);
}