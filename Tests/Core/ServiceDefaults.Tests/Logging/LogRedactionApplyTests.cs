using System.Net.Http.Headers;
using System.Text;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// Redaction is assigned in code over whatever configuration says (ADR 0112), and it reaches the request body a
/// stepped-up request captures.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class LogRedactionApplyTests
{
    private const string VisibleMarker = "visible-marker";

    [Fact(DisplayName = "Should_AssignRedactionWholesale_When_ConfigurationSetsWeakerValues")]
    public async Task Should_AssignRedactionWholesale_When_ConfigurationSetsWeakerValues()
    {
        await using var host = LoggingPipelineHost.Build([
            new("SerilogStepUp:RedactionRegexes:0", "nothing"),
            new("SerilogStepUp:AdditionalSensitiveHeaders:0", "x-foo"),
            new("SerilogStepUp:RedactLogEventProperties", "false")
        ]);

        var options = host.StepUpOptions;

        Assert.Superset(
            new HashSet<string>(LogRedactionDefaultsProbe.ExpectedSensitiveHeaders, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(options.AdditionalSensitiveHeaders, StringComparer.OrdinalIgnoreCase));
        Assert.True(options.RedactLogEventProperties);
        Assert.Equal(LogRedactionDefaultsProbe.RedactionRegexes, options.RedactionRegexes);
    }

    [Fact(DisplayName = "Should_RedactThePasswordAndToken_When_AJsonBodyIsCaptured")]
    public async Task Should_RedactThePasswordAndToken_When_AJsonBodyIsCaptured()
    {
        const string json = $$"""{"name":"{{VisibleMarker}}","password":"hunter2-json","token":"tok-json-secret"}""";

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var body = await CapturedBodyOfAsync(content);

        Assert.Contains(VisibleMarker, body, StringComparison.Ordinal);
        Assert.Contains(LogRedactionDefaultsProbe.Redacted, body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2-json", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-json-secret", body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should_RedactThePassword_When_AFormBodyIsCaptured")]
    public async Task Should_RedactThePassword_When_AFormBodyIsCaptured()
    {
        using var form = new StringContent($"name={VisibleMarker}&password=hunter2-form", Encoding.UTF8);
        form.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        var body = await CapturedBodyOfAsync(form);

        Assert.Contains(VisibleMarker, body, StringComparison.Ordinal);
        Assert.Contains(LogRedactionDefaultsProbe.Redacted, body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2-form", body, StringComparison.Ordinal);
    }

    private static async Task<string> CapturedBodyOfAsync(HttpContent content)
    {
        await using var host = await RequestPipelineHost.StartAsync([
            .. LoggingPipelineHost.Diagnostic(),
            new("SerilogStepUp:CaptureRequestBody", "true")
        ]);

        using (var response = await host.PostAsync(RequestPipelineHost.BodyProbePath, content))
        {
            response.EnsureSuccessStatusCode();
        }

        await LoggingPipelineHost.WaitUntilAsync(() => host.Events.Any(HasRequestBody));
        var completion = Assert.Single(host.Events, HasRequestBody);
        return Assert.IsType<ScalarValue>(completion.Properties["RequestBody"]).Value as string ?? string.Empty;
    }

    private static bool HasRequestBody(LogEvent logEvent) => logEvent.Properties.ContainsKey("RequestBody");
}