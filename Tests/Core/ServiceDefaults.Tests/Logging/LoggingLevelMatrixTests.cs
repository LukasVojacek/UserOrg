using Microsoft.Extensions.Logging;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The level matrix of the logging modes (ADR 0112): what reaches a config-declared sink in Normal mode, under
/// auto step-up and in diagnostic mode, driven through the real StepUpLogging pipeline AddServiceDefaults builds.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class LoggingLevelMatrixTests
{
    private const string OurCategory = "UserOrg.Core.Probe";
    private const string LifecycleCategory = "Microsoft.Hosting.Lifetime";

    [Theory(DisplayName = "Should_ExportTheLifecycleCategoryAtInformation_ExactlyOnce_InEveryMode")]
    [InlineData("normal")]
    [InlineData("step-up")]
    [InlineData("diagnostic")]
    public async Task Should_ExportTheLifecycleCategoryAtInformation_ExactlyOnce_InEveryMode(string mode)
    {
        await using var host = LoggingPipelineHost.Build(mode == "diagnostic" ? LoggingPipelineHost.Diagnostic() : null);
        if (mode == "step-up")
        {
            host.Controller.Trigger();
        }

        var body = host.Log(LifecycleCategory, LogLevel.Information, "Application started. Press Ctrl+C to shut down.");

        Assert.Equal(1, host.CountOf(body));
    }

    [Fact(DisplayName = "Should_ExportTheLifecycleCategory_When_TheLevelSwitchIsAtBaseLevelWarning")]
    public async Task Should_ExportTheLifecycleCategory_When_TheLevelSwitchIsAtBaseLevelWarning()
    {
        await using var host = LoggingPipelineHost.Build();
        Assert.Equal(LogEventLevel.Warning, host.Controller.LevelSwitch.MinimumLevel);

        var body = host.Log(LifecycleCategory, LogLevel.Information);

        var exported = Assert.Single(host.Events, e => LoggingPipelineHost.BodyOf(e) == body);
        Assert.Equal(LogEventLevel.Information, exported.Level);
    }

    [Fact(DisplayName = "Should_DropPollyInformation_And_PassOurWarning_InNormalMode")]
    public async Task Should_DropPollyInformation_And_PassOurWarning_InNormalMode()
    {
        await using var host = LoggingPipelineHost.Build();

        var polly = host.Log("Polly", LogLevel.Information);
        var ours = host.Log(OurCategory, LogLevel.Warning);

        Assert.Equal(0, host.CountOf(polly));
        Assert.Equal(1, host.CountOf(ours));
    }

    [Fact(DisplayName = "Should_PassOurInformation_UnderAutoStepUp")]
    public async Task Should_PassOurInformation_UnderAutoStepUp()
    {
        await using var host = LoggingPipelineHost.Build();
        host.Controller.Trigger();
        Assert.Equal(LogEventLevel.Information, host.Controller.LevelSwitch.MinimumLevel);

        var ours = host.Log(OurCategory, LogLevel.Information);

        Assert.Equal(1, host.CountOf(ours));
    }

    [Theory(DisplayName = "Should_DropFrameworkInformation_UnderAutoStepUp")]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    [InlineData("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker")]
    [InlineData("Duende.AspNetCore.Authentication.OAuth2Introspection")]
    [InlineData("Serilog.AspNetCore.RequestLoggingMiddleware")]
    [InlineData("System.Net.Http.HttpClient.OtlpLogExporter.LogicalHandler")]
    [InlineData("Polly")]
    public async Task Should_DropFrameworkInformation_UnderAutoStepUp(string category)
    {
        await using var host = LoggingPipelineHost.Build();
        host.Controller.Trigger();
        Assert.Equal(LogEventLevel.Information, host.Controller.LevelSwitch.MinimumLevel);

        var framework = host.Log(category, LogLevel.Information);
        var ours = host.Log(OurCategory, LogLevel.Information);

        Assert.Equal(0, host.CountOf(framework));
        Assert.Equal(1, host.CountOf(ours));
    }

    [Fact(DisplayName = "Should_KeepFrameworkCategoriesAtWarning_When_ConfigurationLowersThem")]
    public async Task Should_KeepFrameworkCategoriesAtWarning_When_ConfigurationLowersThem()
    {
        await using var host = LoggingPipelineHost.Build([
            new("Serilog:MinimumLevel:Default", "Information"),
            new("Serilog:MinimumLevel:Override:Microsoft", "Information"),
            new("Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Routing", "Information")
        ]);
        host.Controller.Trigger();

        var routing = host.Log("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Information);
        var mvc = host.Log("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", LogLevel.Information);

        Assert.Equal(0, host.CountOf(routing));
        Assert.Equal(0, host.CountOf(mvc));
    }

    [Fact(DisplayName = "Should_KeepTheFrameworkFloorAtWarning_When_ConfigurationLowersIt")]
    public async Task Should_KeepTheFrameworkFloorAtWarning_When_ConfigurationLowersIt()
    {
        await using var host = LoggingPipelineHost.Build([
            new("SerilogStepUp:CategoryFloors:Polly", "Information"),
            new("SerilogStepUp:CategoryFloors:Microsoft", "Debug")
        ]);
        host.Controller.Trigger();

        var polly = host.Log("Polly", LogLevel.Information);
        var mvc = host.Log("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", LogLevel.Information);

        Assert.Equal("Warning", Assert.Contains("Polly", host.StepUpOptions.CategoryFloors).ToString());
        Assert.Equal("Warning", Assert.Contains("Microsoft", host.StepUpOptions.CategoryFloors).ToString());
        Assert.Equal(0, host.CountOf(polly));
        Assert.Equal(0, host.CountOf(mvc));
    }

    [Theory(DisplayName = "Should_PassFrameworkDebug_InDiagnosticMode")]
    [InlineData("Microsoft.EntityFrameworkCore.Infrastructure")]
    [InlineData("Polly")]
    [InlineData("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker")]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.EntityFrameworkCore.ChangeTracking")]
    [InlineData("Microsoft.EntityFrameworkCore.Query")]
    [InlineData("Microsoft.EntityFrameworkCore.Update")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    public async Task Should_PassFrameworkDebug_InDiagnosticMode(string category)
    {
        await using var host = LoggingPipelineHost.Build(LoggingPipelineHost.Diagnostic());
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);

        var body = host.Log(category, LogLevel.Debug);

        Assert.Equal(1, host.CountOf(body));
    }

    [Theory(DisplayName = "Should_DropTheExporterCategoriesAtInformation_InDiagnosticMode")]
    [InlineData("System.Net.Http.HttpClient.OtlpMetricExporter.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.OtlpLogExporter.ClientHandler")]
    [InlineData("System.Net.Http.HttpClient.OtlpTraceExporter")]
    public async Task Should_DropTheExporterCategoriesAtInformation_InDiagnosticMode(string category)
    {
        await using var host = LoggingPipelineHost.Build(LoggingPipelineHost.Diagnostic());
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);

        var exporter = host.Log(category, LogLevel.Information);
        var control = host.Log("System.Net.Http.HttpClient.vault.LogicalHandler", LogLevel.Debug);

        Assert.Equal(0, host.CountOf(exporter));
        Assert.Equal(1, host.CountOf(control));
    }
}