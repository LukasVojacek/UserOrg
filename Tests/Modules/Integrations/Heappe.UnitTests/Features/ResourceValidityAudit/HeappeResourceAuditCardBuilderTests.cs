using System.Text.Json;
using System.Text.Json.Nodes;

using Heappe.Features.ResourceValidityAudit;

using Xunit;

namespace Heappe.UnitTests.Features.ResourceValidityAudit;

/// <summary>
/// Verifies <see cref="HeappeResourceAuditCardBuilder.BuildCardJson"/> produces a valid
/// Teams Power Automate "Workflows" webhook envelope wrapping an adaptive card that lists
/// only audit findings, grouped by HEAppE instance.
/// </summary>
public sealed class HeappeResourceAuditCardBuilderTests
{
    private static readonly Uri InstanceA = new("https://heappe.example.org/a");
    private static readonly Uri InstanceB = new("https://heappe.example.org/b");

    [Fact(DisplayName = "Should produce valid Teams envelope listing the finding when single Missing result")]
    public void Should_produce_valid_Teams_envelope_When_single_Missing_finding()
    {
        var results = new[]
        {
            new TargetAuditResult(
                "GpuCluster", "PROJ-1", InstanceA, AuditStatus.Missing,
                null, null, null, null, null),
        };

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("message", root.GetProperty("type").GetString());

        var attachments = root.GetProperty("attachments");
        Assert.Equal(1, attachments.GetArrayLength());

        var attachment = attachments[0];
        Assert.Equal(
            "application/vnd.microsoft.card.adaptive",
            attachment.GetProperty("contentType").GetString());

        var content = attachment.GetProperty("content");
        Assert.Equal("AdaptiveCard", content.GetProperty("type").GetString());

        Assert.Contains("GpuCluster", json, StringComparison.Ordinal);
        Assert.Contains("PROJ-1", json, StringComparison.Ordinal);
        Assert.Contains("missing", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Should include expected and actual dates when ValidityMismatch finding")]
    public void Should_include_expected_and_actual_dates_When_ValidityMismatch_finding()
    {
        var results = new[]
        {
            new TargetAuditResult(
                "CpuCluster", "PROJ-2", InstanceA, AuditStatus.ValidityMismatch,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                new DateOnly(2026, 2, 1), new DateOnly(2026, 11, 30),
                null),
        };

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        Assert.Contains("2026-01-01", json, StringComparison.Ordinal);
        Assert.Contains("2026-12-31", json, StringComparison.Ordinal);
        Assert.Contains("2026-02-01", json, StringComparison.Ordinal);
        Assert.Contains("2026-11-30", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should include the reason when CheckFailed finding")]
    public void Should_include_the_reason_When_CheckFailed_finding()
    {
        var results = new[]
        {
            new TargetAuditResult(
                "FpgaCluster", "PROJ-3", InstanceA, AuditStatus.CheckFailed,
                null, null, null, null, "Connection refused by host"),
        };

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        Assert.Contains("Connection refused by host", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should render two instance groups labelled by URL when findings on two HeappeUri")]
    public void Should_render_two_instance_groups_When_findings_on_two_HeappeUri()
    {
        var results = new[]
        {
            new TargetAuditResult(
                "ClusterA", "PROJ-A", InstanceA, AuditStatus.Missing,
                null, null, null, null, null),
            new TargetAuditResult(
                "ClusterB", "PROJ-B", InstanceB, AuditStatus.Missing,
                null, null, null, null, null),
        };

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        var body = CardBody(json);
        var textBlocks = body
            .Where(b => b!["type"]!.GetValue<string>() == "TextBlock")
            .Select(b => b!["text"]!.GetValue<string>())
            .ToList();

        Assert.Contains(textBlocks, t => t == InstanceA.ToString());
        Assert.Contains(textBlocks, t => t == InstanceB.ToString());
        Assert.Contains("ClusterA", json, StringComparison.Ordinal);
        Assert.Contains("ClusterB", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should omit Ok results when mix of findings and Ok")]
    public void Should_omit_Ok_results_When_mix_of_findings_and_Ok()
    {
        var results = new[]
        {
            new TargetAuditResult(
                "HealthyCluster", "PROJ-OK", InstanceA, AuditStatus.Ok,
                null, null, null, null, null),
            new TargetAuditResult(
                "BrokenCluster", "PROJ-BAD", InstanceA, AuditStatus.Missing,
                null, null, null, null, null),
        };

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        Assert.DoesNotContain("HealthyCluster", json, StringComparison.Ordinal);
        Assert.Contains("BrokenCluster", json, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should report correct per-category counts in the header")]
    public void Should_report_correct_per_category_counts_in_header()
    {
        var results = new[]
        {
            new TargetAuditResult("M1", "P", InstanceA, AuditStatus.Missing, null, null, null, null, null),
            new TargetAuditResult("M2", "P", InstanceA, AuditStatus.Missing, null, null, null, null, null),
            new TargetAuditResult("D1", "P", InstanceA, AuditStatus.ValidityMismatch,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1),
                new DateOnly(2026, 1, 2), new DateOnly(2026, 2, 2), null),
        };

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        var header = CardBody(json)[0]!["text"]!.GetValue<string>();
        Assert.Contains("2 missing", header, StringComparison.Ordinal);
        Assert.Contains("1 drift", header, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable", header, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should cap at 50 rendered with overflow note while header counts all when 53 findings")]
    public void Should_cap_at_50_rendered_with_overflow_When_53_findings()
    {
        var results = Enumerable.Range(0, 53)
            .Select(i => new TargetAuditResult(
                $"Cluster{i}", "PROJ", InstanceA, AuditStatus.Missing,
                null, null, null, null, null))
            .ToArray();

        var json = HeappeResourceAuditCardBuilder.BuildCardJson(results);

        // Header reflects the full count.
        var header = CardBody(json)[0]!["text"]!.GetValue<string>();
        Assert.Contains("53 missing", header, StringComparison.Ordinal);

        // Only 50 findings rendered: the 50th index (Cluster50/51/52) must be absent.
        Assert.Contains("Cluster49", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Cluster50", json, StringComparison.Ordinal);

        // Overflow note present for the remaining 3.
        Assert.Contains("…and 3 more", json, StringComparison.Ordinal);
    }

    private static JsonArray CardBody(string json)
    {
        var node = JsonNode.Parse(json)!;
        return node["attachments"]![0]!["content"]!["body"]!.AsArray();
    }
}
