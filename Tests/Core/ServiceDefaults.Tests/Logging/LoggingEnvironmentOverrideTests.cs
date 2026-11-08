using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// Environment variables sit above the production logging defaults, and <c>SerilogStepUp__Mode=Diagnostic</c> set as
/// one is how an operator enters Diagnostic mode (ADR 0114). These tests set process-wide variables.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class LoggingEnvironmentOverrideTests : IDisposable
{
    private readonly Dictionary<string, string?> _saved = [];

    private void Set(string name, string value)
    {
        _saved.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Fact(DisplayName = "Should_LetAnEnvironmentVariableOverrideOneProductionLoggingDefault")]
    public async Task Should_LetAnEnvironmentVariableOverrideOneProductionLoggingDefault()
    {
        Set("SerilogStepUp__StepUpLevel", "Debug");

        await using var host = LoggingPipelineHost.Build();

        var options = host.StepUpOptions;
        Assert.Equal("Debug", options.StepUpLevel);
        Assert.Equal("Warning", options.BaseLevel);
        Assert.Equal(60, options.DurationSeconds);
        Assert.Equal(ProductionLoggingDefaultsTests.DefaultExcludePaths, options.ExcludePaths);
    }

    [Fact(DisplayName = "Should_RunInDiagnosticModeAtDebug_With_TheProductionBufferValues_When_TheModeIsSetAsAnEnvironmentVariable")]
    public async Task Should_RunInDiagnosticModeAtDebug_With_TheProductionBufferValues_When_TheModeIsSetAsAnEnvironmentVariable()
    {
        Set("SerilogStepUp__Mode", "Diagnostic");

        await using var host = LoggingPipelineHost.Build();

        var options = host.StepUpOptions;
        Assert.Equal(StepUpMode.Diagnostic, options.Mode);
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);
        Assert.True(options.EnablePreErrorBuffering);
        Assert.Equal(50, options.PreErrorBufferSize);
        Assert.Equal(256, options.PreErrorMaxContexts);
    }

    [Fact(DisplayName = "Should_StepUpToInformation_When_AnErrorIsLoggedAfterTheDiagnosticWindowExpires")]
    public async Task Should_StepUpToInformation_When_AnErrorIsLoggedAfterTheDiagnosticWindowExpires()
    {
        Set("SerilogStepUp__Mode", "Diagnostic");
        Set("SerilogStepUp__DiagnosticDurationMinutes", "30");
        var time = new FakeTimeProvider();

        await using var host = LoggingPipelineHost.Build(
            beforeServiceDefaults: builder => builder.Services.AddSingleton<TimeProvider>(time));
        Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);

        time.Advance(TimeSpan.FromMinutes(30));
        await LoggingPipelineHost.WaitUntilAsync(() => host.Controller.LevelSwitch.MinimumLevel == LogEventLevel.Warning);
        Assert.Equal(LogEventLevel.Warning, host.Controller.LevelSwitch.MinimumLevel);

        using (new System.Diagnostics.Activity("post-expiry").Start())
        {
            host.LoggerFactory.CreateLogger("UserOrg.Core.Probe").LogError("{Body}", $"failure {Guid.NewGuid():N}");
        }

        await LoggingPipelineHost.WaitUntilAsync(() => host.Controller.LevelSwitch.MinimumLevel == LogEventLevel.Information, TimeSpan.FromSeconds(1));
        Assert.Equal(LogEventLevel.Information, host.Controller.LevelSwitch.MinimumLevel);
    }
}