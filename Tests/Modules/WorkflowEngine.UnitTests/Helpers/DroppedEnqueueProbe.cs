using System.Diagnostics.Metrics;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// Listens for the <see cref="System.Diagnostics.Metrics"/> instrument that records a dropped enqueue.
/// <para>
/// Deliberately lenient about naming: it enables every published instrument whose <b>name</b> contains
/// "drop" (case-insensitive), whatever meter it belongs to, and sums the measurements. The implementation
/// is free to choose the meter name, the instrument name and the measurement type (<c>long</c>, <c>int</c>
/// or <c>double</c>); only "an instrument whose name says dropped is incremented" is pinned.
/// </para>
/// <para>
/// <see cref="MeterListener.Start"/> also publishes instruments created before the probe existed, so a
/// static <see cref="Meter"/> held inside the adapter is observed as well.
/// </para>
/// </summary>
internal sealed class DroppedEnqueueProbe : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Lock _gate = new();
    private readonly List<string> _published = [];
    private long _dropped;

    public DroppedEnqueueProbe()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            lock (_gate)
            {
                _published.Add($"{instrument.Meter.Name} :: {instrument.Name}");
            }

            if (instrument.Name.Contains("drop", StringComparison.OrdinalIgnoreCase))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((_, m, _, _) => Interlocked.Add(ref _dropped, m));
        _listener.SetMeasurementEventCallback<int>((_, m, _, _) => Interlocked.Add(ref _dropped, m));
        _listener.SetMeasurementEventCallback<double>((_, m, _, _) => Interlocked.Add(ref _dropped, (long)m));
        _listener.Start();
    }

    /// <summary>Sum of every measurement recorded on a "drop"-named instrument since the probe started.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>Every published meter/instrument pair — the diagnostic printed by a failing assert.</summary>
    public string PublishedInstruments
    {
        get
        {
            lock (_gate)
            {
                return _published.Count == 0 ? "(none)" : string.Join(", ", _published);
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}
