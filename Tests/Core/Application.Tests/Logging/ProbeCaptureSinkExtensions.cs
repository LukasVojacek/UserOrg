using System.Collections.Concurrent;

using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace UserOrganizationManagement.Application.Tests.Logging;

/// <summary>
/// A Serilog sink declared through <c>Serilog:Using</c> and <c>Serilog:WriteTo</c> configuration, the way a
/// deployment declares its own sinks. Every instance built for one <c>runId</c> records into the same list.
/// </summary>
public static class ProbeCaptureSinkExtensions
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<LogEvent>> Runs = new();

    public static LoggerConfiguration ProbeCapture(this LoggerSinkConfiguration sinkConfiguration, string runId)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        return sinkConfiguration.Sink(new ProbeCaptureSink(EventsOf(runId)));
    }

    internal static ConcurrentQueue<LogEvent> EventsOf(string runId) => Runs.GetOrAdd(runId, static _ => new());

    private sealed class ProbeCaptureSink(ConcurrentQueue<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);
    }
}