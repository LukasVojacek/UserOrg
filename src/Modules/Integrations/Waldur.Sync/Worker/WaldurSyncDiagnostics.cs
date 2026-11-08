using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Waldur.Sync.Worker;

/// <summary>
/// The single <see cref="ActivitySource"/> + <see cref="Meter"/> named <c>Waldur.Sync</c> and the
/// iteration instruments (ADR-0058). The names are consts so the ServiceDefaults registration
/// (B12) can subscribe to them without a hard reference. The SHA-map-size gauge is observable: each
/// worker registers a size accessor for its instance on start and unregisters on stop.
/// </summary>
public static class WaldurSyncDiagnostics
{
    /// <summary>The shared ActivitySource / Meter name (subscribe to this in ServiceDefaults, B12).</summary>
    public const string SourceName = "Waldur.Sync";

    /// <summary>Metric name suffix: iterations completed.</summary>
    public const string IterationsMetricSuffix = "Iterations";

    /// <summary>Metric name suffix: iteration wall-clock duration in milliseconds.</summary>
    public const string IterationDurationMetricSuffix = "IterationDuration";

    /// <summary>Metric name suffix: current SHA breadcrumb-map size.</summary>
    public const string ShaMapSizeMetricSuffix = "ShaMapSize";

    /// <summary>The instance tag key applied to every metric + activity.</summary>
    public const string InstanceTag = "waldur.instance";

    /// <summary>The activity name for one sync iteration.</summary>
    public const string IterationActivityName = "waldur.sync.iteration";

    /// <summary>The activity name for one provider/project entity span (ADR-0066).</summary>
    public const string ProjectActivityName = "waldur.sync.project";

    /// <summary>The activity name for one resource entity span (ADR-0066).</summary>
    public const string ResourceActivityName = "waldur.sync.resource";

    /// <summary>The activity name for one user/membership entity span (ADR-0066).</summary>
    public const string UserActivityName = "waldur.sync.user";

    /// <summary>The tag key carrying an entity's remote (Waldur) id on its span (ADR-0066).</summary>
    public const string ExternalIdTag = "waldur.external_id";

    /// <summary>The tag key carrying an entity's type on its span (ADR-0066).</summary>
    public const string EntityTypeTag = "waldur.entity_type";

    /// <summary>The entity-type tag value for a resource span (resources are not a top-level SHA key).</summary>
    public const string ResourceEntityType = "Resource";

    /// <summary>The shared activity source (registered in ServiceDefaults, B12).</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>The shared meter (registered in ServiceDefaults, B12).</summary>
    public static readonly Meter Meter = new(SourceName);

    /// <summary>
    /// One instance's named counter/histogram pair. Kept per <c>ServiceName</c> (not per logical
    /// instance name) because <c>WaldurInstanceIdentityOptions.ServiceName</c> is the stable DB/upsert
    /// discriminator (e.g. "EFP-Waldur", "Puhuri-Waldur") baked directly into the metric name.
    /// </summary>
    private sealed class ServiceInstruments
    {
        public required Counter<long> Iterations { get; init; }
        public required Histogram<double> IterationDuration { get; init; }
    }

    private static readonly ConcurrentDictionary<string, ServiceInstruments> InstrumentsByServiceName =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, Func<int>> MapSizeAccessors =
        new(StringComparer.OrdinalIgnoreCase);

    // ObservableGauge instruments cannot be renamed/removed once created — kept alive for the process
    // lifetime, one per ServiceName, created lazily on first registration.
    private static readonly ConcurrentDictionary<string, ObservableGauge<int>> MapSizeGauges =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a metric name in the <c>Waldur.{serviceName}.{suffix}</c> style so each configured
    /// integration (EFP, Puhuri, …) gets its own distinctly-named metric series instead of one shared
    /// name disambiguated only by a tag.
    /// </summary>
    public static string MetricName(string serviceName, string suffix) => $"Waldur.{serviceName}.{suffix}";

    /// <summary>Records one completed iteration under <paramref name="serviceName"/>'s own named instruments.</summary>
    public static void RecordIteration(string serviceName, TimeSpan duration)
    {
        var instruments = InstrumentsByServiceName.GetOrAdd(serviceName, CreateInstruments);
        instruments.Iterations.Add(1);
        instruments.IterationDuration.Record(duration.TotalMilliseconds);
    }

    private static ServiceInstruments CreateInstruments(string serviceName) => new()
    {
        Iterations = Meter.CreateCounter<long>(
            MetricName(serviceName, IterationsMetricSuffix), unit: "{iteration}"),
        IterationDuration = Meter.CreateHistogram<double>(
            MetricName(serviceName, IterationDurationMetricSuffix), unit: "ms"),
    };

    /// <summary>
    /// Registers (or replaces) the SHA-map-size accessor for <paramref name="serviceName"/> and — on
    /// first registration — creates that service's own <c>Waldur.{serviceName}.ShaMapSize</c> gauge.
    /// </summary>
    public static void RegisterMapSize(string serviceName, Func<int> accessor)
    {
        MapSizeAccessors[serviceName] = accessor;
        MapSizeGauges.GetOrAdd(serviceName, sn => Meter.CreateObservableGauge(
            MetricName(sn, ShaMapSizeMetricSuffix),
            () => MapSizeAccessors.TryGetValue(sn, out var current) ? current() : 0,
            unit: "{entry}"));
    }

    /// <summary>
    /// Removes a service's SHA-map-size accessor (worker stop). The gauge instrument itself stays
    /// registered — .NET Meters cannot unregister an instrument — but reports 0 once unregistered.
    /// </summary>
    public static void UnregisterMapSize(string serviceName) =>
        MapSizeAccessors.TryRemove(serviceName, out _);

    /// <summary>
    /// Opens a child entity span under the current Activity (ADR-0066): same TraceId, a unique SpanId,
    /// tagged with instance + entity type + remote id. Returns null when no listener is registered
    /// (never in production, where the <c>ParentBased(AlwaysOn)</c> sampler always records).
    /// </summary>
    public static Activity? StartEntitySpan(string activityName, string instance, string entityType, string externalId)
    {
        var span = ActivitySource.StartActivity(activityName);
        span?.SetTag(InstanceTag, instance);
        span?.SetTag(EntityTypeTag, entityType);
        span?.SetTag(ExternalIdTag, externalId);
        return span;
    }
}

/// <summary>The <c>phase</c> tag values on per-item failure logs (ADR-0058).</summary>
public static class WaldurSyncPhase
{
    public const string Fetch = "fetch";
    public const string Map = "map";
    public const string Diff = "diff";
    public const string Publish = "publish";
    public const string Handler = "handler";
    public const string Heappe = "heappe";
}