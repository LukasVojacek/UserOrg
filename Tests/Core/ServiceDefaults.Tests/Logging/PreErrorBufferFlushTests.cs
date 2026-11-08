using Microsoft.Extensions.Logging;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// In Normal mode the pre-error buffer holds a trace's Information and exports it once when an Error follows in
/// the same trace, at most PreErrorBufferSize events (QS-05). Framework categories, the EF and Hosting.Diagnostics
/// ones included, are listed in PreErrorBufferExcludeCategories: their events below Warning are never held, so an
/// Error flush exports none of them in Normal mode (QS-04, #334), while Diagnostic mode still exports them at Debug.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class PreErrorBufferFlushTests
{
    private const string OurCategory = "UserOrg.Core.Probe";
    private const string FloorCategory = "Microsoft.EntityFrameworkCore.Infrastructure";
    private const int BufferSize = 50;

    public static TheoryData<string> ExcludedCategories =>
    [
        "Microsoft.EntityFrameworkCore.Database.Command",
        "Microsoft.EntityFrameworkCore.ChangeTracking",
        "Microsoft.EntityFrameworkCore.Query",
        "Microsoft.EntityFrameworkCore.Update",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
    ];

    [Fact(DisplayName = "Should_ExportEveryHeldEventExactlyOnce_When_ErrorsFollowInTheTrace")]
    public async Task Should_ExportEveryHeldEventExactlyOnce_When_ErrorsFollowInTheTrace()
    {
        await using var host = LoggingPipelineHost.Build();
        using var trace = new System.Diagnostics.Activity("held-request").Start();
        string[] held = [.. Enumerable.Range(0, 3).Select(_ => host.Log(OurCategory, LogLevel.Information))];
        Assert.All(held, body => Assert.Equal(0, host.CountOf(body)));

        host.Log(OurCategory, LogLevel.Error);
        await LoggingPipelineHost.WaitUntilAsync(() => held.All(body => host.CountOf(body) > 0));
        host.Log(OurCategory, LogLevel.Error);
        await Task.Delay(200);

        Assert.All(held, body => Assert.Equal(1, host.CountOf(body)));
    }

    [Fact(DisplayName = "Should_ExportTheLastFiftyHeldEvents_When_AnErrorFollowsSixty")]
    public async Task Should_ExportTheLastFiftyHeldEvents_When_AnErrorFollowsSixty()
    {
        await using var host = LoggingPipelineHost.Build();
        using var trace = new System.Diagnostics.Activity("overflowing-request").Start();
        string[] held = [.. Enumerable.Range(0, 60).Select(_ => host.Log(OurCategory, LogLevel.Information))];

        host.Log(OurCategory, LogLevel.Error);
        await LoggingPipelineHost.WaitUntilAsync(() => host.CountOf(held[^1]) > 0);
        await Task.Delay(200);

        Assert.Equal(BufferSize, held.Count(body => host.CountOf(body) == 1));
        Assert.All(held[^BufferSize..], body => Assert.Equal(1, host.CountOf(body)));
    }

    [Fact(DisplayName = "Should_ExportTheLifecycleEventOnce_When_AnErrorFollowsInTheTrace")]
    public async Task Should_ExportTheLifecycleEventOnce_When_AnErrorFollowsInTheTrace()
    {
        await using var host = LoggingPipelineHost.Build();
        using var trace = new System.Diagnostics.Activity("lifecycle-request").Start();
        var lifecycle = host.Log("Microsoft.Hosting.Lifetime", LogLevel.Information);
        var ours = host.Log(OurCategory, LogLevel.Information);

        host.Log(OurCategory, LogLevel.Error);
        await LoggingPipelineHost.WaitUntilAsync(() => host.CountOf(ours) > 0);
        await Task.Delay(200);

        Assert.Equal(1, host.CountOf(ours));
        Assert.Equal(1, host.CountOf(lifecycle));
    }

    [Theory(DisplayName = "Should_ExportNoFrameworkInformation_But_TheApplicationInformation_When_AnErrorFollowsInNormalMode")]
    [MemberData(nameof(ExcludedCategories))]
    [InlineData(FloorCategory)]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    [InlineData("Polly")]
    public async Task Should_ExportNoFrameworkInformation_But_TheApplicationInformation_When_AnErrorFollowsInNormalMode(string frameworkCategory)
    {
        await using var host = LoggingPipelineHost.Build();
        Assert.Equal(LogEventLevel.Warning, host.Controller.LevelSwitch.MinimumLevel);
        using var trace = new System.Diagnostics.Activity("framework-request");
        trace.Start();
        var framework = host.Log(frameworkCategory, LogLevel.Information);
        var ours = host.Log(OurCategory, LogLevel.Information);

        host.Log(OurCategory, LogLevel.Error);
        await LoggingPipelineHost.WaitUntilAsync(() => host.CountOf(ours) > 0);
        await Task.Delay(200);

        Assert.Equal(1, host.CountOf(ours));
        Assert.Equal(0, host.CountOf(framework));
    }

    [Theory(DisplayName = "Should_ExportTheDebugEvent_When_AnErrorFollowsInDiagnosticMode")]
    [MemberData(nameof(ExcludedCategories))]
    public async Task Should_ExportTheDebugEvent_When_AnErrorFollowsInDiagnosticMode(string excludedCategory)
    {
        await using var host = LoggingPipelineHost.Build(LoggingPipelineHost.Diagnostic());
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);
        using var trace = new System.Diagnostics.Activity("sql-request").Start();
        var debug = host.Log(excludedCategory, LogLevel.Debug);

        host.Log(OurCategory, LogLevel.Error);
        await Task.Delay(200);

        Assert.Equal(1, host.CountOf(debug));
    }
}
