using System.Globalization;
using System.Reflection;

using Microsoft.Extensions.Options;

using Serilog.Events;

namespace UserOrganizationManagement.Application.Tests.Logging;

/// <summary>
/// Runs Program's entry point itself, not through WebApplicationFactory, so Program's own catch and finally run
/// when startup fails after the host is built.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ProgramStartupFailureLogTests : IDisposable
{
    private const string SinkKey = "ProbeCapture";
    private const string FatalMessage = "Application terminated unexpectedly";

    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, string?> _previousEnvironment = [];

    public ProgramStartupFailureLogTests()
    {
        ProgramBootEnvironment.Apply();
        SetEnvironment($"Serilog__Using__{SinkKey}", typeof(ProbeCaptureSinkExtensions).Assembly.GetName().Name);
        SetEnvironment($"Serilog__WriteTo__{SinkKey}__Name", nameof(ProbeCaptureSinkExtensions.ProbeCapture));
        SetEnvironment($"Serilog__WriteTo__{SinkKey}__Args__runId", _runId);
        SetEnvironment("Scalar__Title", " ");
    }

    public void Dispose()
    {
        foreach (var (key, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task Should_RethrowAndLogOneFatalThroughTheHostLogger_When_StartupFailsAfterTheHostIsBuilt()
    {
        var entryPoint = typeof(Program).Assembly.EntryPoint!;

        var thrown = await Assert.ThrowsAsync<TargetInvocationException>(
            () => Task.Run(() => entryPoint.Invoke(null, [Array.Empty<string>()])));

        Assert.IsType<OptionsValidationException>(thrown.InnerException);
        var fatal = Assert.Single(
            ProbeCaptureSinkExtensions.EventsOf(_runId),
            logEvent => logEvent.Level == LogEventLevel.Fatal);
        Assert.Equal(FatalMessage, fatal.RenderMessage(CultureInfo.InvariantCulture));
        Assert.Same(thrown.InnerException, fatal.Exception);
    }

    private void SetEnvironment(string key, string? value)
    {
        _previousEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }
}
