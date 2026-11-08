using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// Builds the MS Teams adaptive-card payload for the HEAppE resource validity audit.
/// Only findings (non-<see cref="AuditStatus.Ok"/> results) are rendered; results are grouped
/// by HEAppE instance and capped at <see cref="MaxRenderedFindings"/> rendered entries.
/// </summary>
internal static class HeappeResourceAuditCardBuilder
{
    private const int MaxRenderedFindings = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string BuildCardJson(IReadOnlyCollection<TargetAuditResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var findings = results.Where(r => r.IsFinding).ToList();

        var body = new JsonArray { BuildHeader(findings) };

        var rendered = findings.Take(MaxRenderedFindings).ToList();
        foreach (var group in rendered.GroupBy(f => f.HeappeUri))
        {
            body.Add(BuildInstanceHeader(group.Key));
            foreach (var finding in group)
            {
                body.Add(BuildFindingBlock(finding));
            }
        }

        var overflow = findings.Count - rendered.Count;
        if (overflow > 0)
        {
            body.Add(TextBlock($"…and {overflow} more", weight: "Bolder"));
        }

        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
        };

        var envelope = new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray
            {
                new JsonObject
                {
                    ["contentType"] = "application/vnd.microsoft.card.adaptive",
                    ["content"] = card,
                },
            },
        };

        return envelope.ToJsonString(SerializerOptions);
    }

    private static JsonObject BuildHeader(IReadOnlyCollection<TargetAuditResult> findings)
    {
        var missing = findings.Count(f => f.Status is AuditStatus.Missing);
        var drift = findings.Count(f => f.Status is AuditStatus.ValidityMismatch);
        var unreachable = findings.Count(f => f.Status is AuditStatus.CheckFailed);

        var parts = new List<string>();
        if (missing > 0)
        {
            parts.Add($"{missing} missing");
        }

        if (drift > 0)
        {
            parts.Add($"{drift} drift");
        }

        if (unreachable > 0)
        {
            parts.Add($"{unreachable} unreachable");
        }

        var summary = parts.Count > 0 ? string.Join(" · ", parts) : "no findings";
        return TextBlock($"HEAppE resource audit: {summary}", weight: "Bolder", size: "Large");
    }

    private static JsonObject BuildInstanceHeader(Uri instance) =>
        TextBlock(instance.ToString(), weight: "Bolder");

    private static JsonObject BuildFindingBlock(TargetAuditResult finding)
    {
        var text = finding.Status switch
        {
            AuditStatus.Missing =>
                $"{finding.ResourceName} ({finding.ProjectShortName}) — missing",
            AuditStatus.ValidityMismatch =>
                $"{finding.ResourceName} ({finding.ProjectShortName}) — validity mismatch: expected {FormatRange(finding.ExpectedStart, finding.ExpectedEnd)}, actual {FormatRange(finding.ActualStart, finding.ActualEnd)}",
            AuditStatus.CheckFailed =>
                $"{finding.ResourceName} ({finding.ProjectShortName}) — check failed: {finding.Reason ?? "unknown error"}",
            _ => $"{finding.ResourceName} ({finding.ProjectShortName})",
        };

        return TextBlock(text, wrap: true);
    }

    private static string FormatRange(DateOnly? start, DateOnly? end)
    {
        var from = start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        var to = end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        return $"{from} → {to}";
    }

    private static JsonObject TextBlock(
        string text,
        string? weight = null,
        string? size = null,
        bool wrap = false)
    {
        var block = new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = text,
        };

        if (weight is not null)
        {
            block["weight"] = weight;
        }

        if (size is not null)
        {
            block["size"] = size;
        }

        if (wrap)
        {
            block["wrap"] = true;
        }

        return block;
    }
}