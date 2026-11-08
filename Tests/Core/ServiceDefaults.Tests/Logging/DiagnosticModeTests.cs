using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// Diagnostic mode is StepUpLogging's own <c>SerilogStepUp:Mode=Diagnostic</c> (QS-03): the switch sits at Debug and
/// the framework category floors are lifted for DiagnosticDurationMinutes, the library says so once at entry and
/// once at expiry, and after expiry the host runs as Auto at BaseLevel with its floors back.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class DiagnosticModeTests
{
    private const int DurationMinutes = 30;
    private const string OurCategory = "UserOrg.Core.Probe";
    private const string EntryTemplate = "StepUp Diagnostic mode active";
    private const string ExpiryTemplate = "StepUp Diagnostic mode ended";

    private static LoggingPipelineHost BuildDiagnostic(FakeTimeProvider time) =>
        LoggingPipelineHost.Build(
            LoggingPipelineHost.Diagnostic(DurationMinutes),
            beforeServiceDefaults: builder => builder.Services.AddSingleton<TimeProvider>(time));

    private static async Task ExpireAsync(LoggingPipelineHost host, FakeTimeProvider time)
    {
        time.Advance(TimeSpan.FromMinutes(DurationMinutes));
        await LoggingPipelineHost.WaitUntilAsync(() => host.Controller.LevelSwitch.MinimumLevel == LogEventLevel.Warning);
    }

    private static int CountOfTemplate(LoggingPipelineHost host, string templateStart) =>
        host.Events.Count(e => e.Level == LogEventLevel.Warning && e.MessageTemplate.Text.StartsWith(templateStart, StringComparison.Ordinal));

    [Fact(DisplayName = "Should_WriteOneWarningAtEntry_And_OneAtExpiry")]
    public async Task Should_WriteOneWarningAtEntry_And_OneAtExpiry()
    {
        var time = new FakeTimeProvider();
        await using var host = BuildDiagnostic(time);
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);
        await LoggingPipelineHost.WaitUntilAsync(() => CountOfTemplate(host, EntryTemplate) > 0);

        Assert.Equal(1, CountOfTemplate(host, EntryTemplate));
        Assert.Equal(0, CountOfTemplate(host, ExpiryTemplate));

        await ExpireAsync(host, time);
        await LoggingPipelineHost.WaitUntilAsync(() => CountOfTemplate(host, ExpiryTemplate) > 0);

        Assert.Equal(1, CountOfTemplate(host, EntryTemplate));
        Assert.Equal(1, CountOfTemplate(host, ExpiryTemplate));
    }

    [Theory(DisplayName = "Should_ExportFrameworkDebug_While_DiagnosticModeIsActive")]
    [InlineData("Polly")]
    [InlineData("Yarp.ReverseProxy.Forwarder.HttpForwarder")]
    [InlineData("System.Net.Http.HttpClient.vault.LogicalHandler")]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    public async Task Should_ExportFrameworkDebug_While_DiagnosticModeIsActive(string category)
    {
        await using var host = BuildDiagnostic(new FakeTimeProvider());
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);

        var body = host.Log(category, LogLevel.Debug);

        Assert.Equal(1, host.CountOf(body));
    }

    [Fact(DisplayName = "Should_RestoreTheFrameworkFloors_And_AutoStepUp_After_TheDiagnosticWindowExpires")]
    public async Task Should_RestoreTheFrameworkFloors_And_AutoStepUp_After_TheDiagnosticWindowExpires()
    {
        var time = new FakeTimeProvider();
        await using var host = BuildDiagnostic(time);
        await ExpireAsync(host, time);
        Assert.Equal(LogEventLevel.Warning, host.Controller.LevelSwitch.MinimumLevel);

        host.Controller.Trigger();
        Assert.Equal(LogEventLevel.Information, host.Controller.LevelSwitch.MinimumLevel);

        var polly = host.Log("Polly", LogLevel.Information);
        var ours = host.Log(OurCategory, LogLevel.Information);

        Assert.Equal(0, host.CountOf(polly));
        Assert.Equal(1, host.CountOf(ours));
    }
}