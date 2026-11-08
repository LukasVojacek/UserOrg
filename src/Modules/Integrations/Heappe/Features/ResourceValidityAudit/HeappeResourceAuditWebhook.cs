using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// Posts the findings card to the configured MS Teams webhook. Best-effort: a missing/invalid URL or a failed
/// POST is logged and counted, never thrown — it must not crash the worker or the request.
/// </summary>
internal sealed class HeappeResourceAuditWebhook(
    IHttpClientFactory httpClientFactory,
    IOptions<HeappeResourceAuditOptions> options,
    ILogger<HeappeResourceAuditWebhook> logger)
{
    internal const string HttpClientName = "HeappeResourceAuditWebhook";

    /// <summary>Builds and POSTs the card. Returns true only on a successful HTTP response.</summary>
    internal async Task<bool> TryPostAsync(IReadOnlyCollection<TargetAuditResult> findings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(findings);

        // WebhookUrl is validated here (not at startup) so the audit stays usable when the feature is off
        // and no webhook is configured — it is only required at the moment we actually need to notify.
        if (options.Value.WebhookUrl is not { IsAbsoluteUri: true } webhookUrl)
        {
            logger.LogError(
                "HEAppE resource audit has {Count} finding(s) but '{Section}:WebhookUrl' is not a valid absolute URI; notification skipped.",
                findings.Count, HeappeResourceAuditOptions.SectionName);
            HeappeResourceAuditTelemetry.WebhookFailures.Add(1);
            return false;
        }

        try
        {
            var payload = HeappeResourceAuditCardBuilder.BuildCardJson(findings);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(webhookUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("HEAppE resource audit webhook POST returned HTTP {StatusCode}.", (int)response.StatusCode);
                HeappeResourceAuditTelemetry.WebhookFailures.Add(1);
                return false;
            }

            logger.LogInformation("HEAppE resource audit posted {Count} finding(s) to the Teams webhook.", findings.Count);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "HEAppE resource audit webhook POST failed.");
            HeappeResourceAuditTelemetry.WebhookFailures.Add(1);
            return false;
        }
    }
}