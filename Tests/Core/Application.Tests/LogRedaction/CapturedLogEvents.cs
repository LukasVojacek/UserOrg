using System.Globalization;

using Serilog.Events;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

internal static class CapturedLogEvents
{
    public static bool IsRequestSummary(this LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("IsRequestSummary", out var value) && value is ScalarValue { Value: true };

    public static bool HasTemplatePrefix(this LogEvent logEvent, string prefix) =>
        logEvent.MessageTemplate.Text.StartsWith(prefix, StringComparison.Ordinal);

    public static bool IsFrom(this LogEvent logEvent, string sourceContextSuffix) =>
        logEvent.StringProperty("SourceContext")?.EndsWith(sourceContextSuffix, StringComparison.Ordinal) ?? false;

    public static string? StringProperty(this LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: { } scalar }
            ? Convert.ToString(scalar, CultureInfo.InvariantCulture)
            : null;

    public static bool Mentions(this LogEvent logEvent, string marker) =>
        logEvent.RenderMessage(CultureInfo.InvariantCulture).Contains(marker, StringComparison.OrdinalIgnoreCase)
        || logEvent.Properties.Values.Any(value => Mentions(value, marker))
        || (logEvent.Exception?.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase) ?? false);

    public static void AssertNoneMentions(this IEnumerable<LogEvent> events, string marker)
    {
        var leaking = events.Where(logEvent => logEvent.Mentions(marker)).Select(Describe).ToList();
        Assert.True(
            leaking.Count == 0,
            $"{leaking.Count} captured event(s) carry '{marker}':{Environment.NewLine}{string.Join(Environment.NewLine, leaking)}");
    }

    public static string Describe(this LogEvent logEvent) =>
        $"[{logEvent.Level}] {logEvent.StringProperty("SourceContext")}: {logEvent.RenderMessage(CultureInfo.InvariantCulture)}";

    private static bool Mentions(LogEventPropertyValue value, string marker) => value switch
    {
        ScalarValue scalar => Convert.ToString(scalar.Value, CultureInfo.InvariantCulture)?.Contains(marker, StringComparison.OrdinalIgnoreCase) ?? false,
        SequenceValue sequence => sequence.Elements.Any(element => Mentions(element, marker)),
        StructureValue structure => structure.Properties.Any(property => Mentions(property.Value, marker)),
        DictionaryValue dictionary => dictionary.Elements.Any(pair => Mentions(pair.Key, marker) || Mentions(pair.Value, marker)),
        _ => value.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase),
    };
}