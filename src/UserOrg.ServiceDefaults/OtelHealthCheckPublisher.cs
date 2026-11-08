using System.Diagnostics.Metrics;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UserOrg.ServiceDefaults;

/// <summary>
/// Publishes health check results as an OpenTelemetry ObservableGauge metric
/// under the <c>UserOrg.HealthChecks</c> meter.
/// <para>
/// Metric name: <c>health_check_status</c> — values are 1 (Healthy), 0 (Degraded), or -1 (Unhealthy).
/// Each measurement carries a <c>check.name</c> tag with the registered check name.
/// </para>
/// </summary>
internal sealed class OtelHealthCheckPublisher : IHealthCheckPublisher
{
    private volatile HealthReport? _latestReport;

    public OtelHealthCheckPublisher(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create("UserOrg.HealthChecks");
        meter.CreateObservableGauge<int>(
            name: "health_check_status",
            observeValues: GetMeasurements,
            unit: "1",
            description: "Health check result per named check: 1=Healthy, 0=Degraded, -1=Unhealthy");
    }

    private IEnumerable<Measurement<int>> GetMeasurements()
    {
        var report = _latestReport;
        if (report is null)
        {
            return [];
        }

        return report.Entries.Select(e => new Measurement<int>(
            e.Value.Status switch
            {
                HealthStatus.Healthy => 1,
                HealthStatus.Degraded => 0,
                _ => -1
            },
            new KeyValuePair<string, object?>("check.name", e.Key)));
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        _latestReport = report;
        return Task.CompletedTask;
    }
}