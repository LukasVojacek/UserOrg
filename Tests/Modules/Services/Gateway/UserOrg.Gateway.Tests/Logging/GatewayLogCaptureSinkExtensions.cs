using System.Collections.Concurrent;

using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace UserOrg.Gateway.Tests.Logging;

/// <summary>
/// A Serilog sink declared through <c>Serilog:Using</c> and <c>Serilog:WriteTo</c> configuration, the only kind
/// StepUpLogging also hands its bypass-routed events (the request summary). Every instance built for one
/// <c>runId</c> records into the same queue.
/// </summary>
public static class GatewayLogCaptureSinkExtensions
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<LogEvent>> Runs = new();

    public static LoggerConfiguration GatewayLogCapture(this LoggerSinkConfiguration sinkConfiguration, string runId)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        return sinkConfiguration.Sink(new CaptureSink(EventsOf(runId)));
    }

    internal static ConcurrentQueue<LogEvent> EventsOf(string runId) => Runs.GetOrAdd(runId, static _ => new());

    internal static void Forget(string runId) => Runs.TryRemove(runId, out _);

    private sealed class CaptureSink(ConcurrentQueue<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);
    }
}