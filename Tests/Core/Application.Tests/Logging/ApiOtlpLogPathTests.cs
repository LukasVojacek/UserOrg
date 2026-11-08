using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrganizationManagement.Application.Tests.TrustedEdge;

namespace UserOrganizationManagement.Application.Tests.Logging;

/// <summary>
/// The API reaches its config-declared Serilog sinks and the OTLP log path through the StepUpLogging pipeline alone,
/// once each.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed partial class ApiOtlpLogPathTests : IAsyncLifetime
{
    private const string ProbeCategory = "UserOrg.Api.Probe";
    private const string SinkKey = "ProbeCapture";

    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, string?> _previousEnvironment = [];
    private OtlpHttpLogListener _otlp = null!;

    public async ValueTask InitializeAsync()
    {
        _otlp = await OtlpHttpLogListener.StartAsync();

        SetEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", _otlp.Endpoint);
        SetEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        SetEnvironment($"Serilog__Using__{SinkKey}", typeof(ProbeCaptureSinkExtensions).Assembly.GetName().Name);
        SetEnvironment($"Serilog__WriteTo__{SinkKey}__Name", nameof(ProbeCaptureSinkExtensions.ProbeCapture));
        SetEnvironment($"Serilog__WriteTo__{SinkKey}__Args__runId", _runId);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (key, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        await _otlp.DisposeAsync();
    }

    [Fact]
    public async Task Should_ReachTheConfigDeclaredSinkAndTheOtlpLogPathExactlyOnce_When_TheApiHostLogsAWarning()
    {
        var probeId = Guid.NewGuid();
        var body = $"OTLP log path probe {probeId}";

        await using var factory = new TrustedEdgeBootFactory();
        LogProbe(factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ProbeCategory), probeId);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!_otlp.LogBodies.Contains(body) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Multiple(
            () => Assert.Single(
                ProbeCaptureSinkExtensions.EventsOf(_runId),
                logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture) == body),
            () => Assert.Single(_otlp.LogBodies, logBody => logBody == body));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OTLP log path probe {ProbeId}")]
    private static partial void LogProbe(ILogger logger, Guid probeId);

    private void SetEnvironment(string key, string? value)
    {
        _previousEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }
}