using System.Diagnostics.Metrics;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using UserOrg.ServiceDefaults;

namespace ServiceDefaults.Tests;

/// <summary>
/// Verifies that <see cref="OtelHealthCheckPublisher"/> emits the correct
/// <c>health_check_status</c> gauge value for every possible <see cref="HealthStatus"/>.
/// The publisher is resolved through the DI container so the test is independent of
/// the internal visibility of the class.
/// </summary>
public sealed class OtelHealthCheckPublisherTests
{
    [Theory(DisplayName = "Should_EmitCorrectGaugeValue_For_HealthStatus")]
    [InlineData(HealthStatus.Healthy, 1)]
    [InlineData(HealthStatus.Degraded, 0)]
    [InlineData(HealthStatus.Unhealthy, -1)]
    public async Task Should_EmitCorrectGaugeValue_For_HealthStatus(
        HealthStatus status, int expectedValue)
    {
        // Arrange ─────────────────────────────────────────────────────────────
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "ServiceDefaults.Tests"
        });

        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();

        // Grab the registered publisher (OtelHealthCheckPublisher implements IHealthCheckPublisher)
        var publishers = app.Services.GetServices<IHealthCheckPublisher>().ToList();
        Assert.Single(publishers);
        var publisher = publishers[0];

        var report = BuildReport("check-under-test", status);

        // Start a MeterListener before publishing so we capture the ObservableGauge callback
        var captured = new List<(int Value, string CheckName)>();
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "UserOrg.HealthChecks" &&
                instrument.Name == "health_check_status")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<int>((_, measurement, tags, _) =>
        {
            var checkName = string.Empty;
            foreach (var tag in tags)
            {
                if (tag.Key == "check.name")
                {
                    checkName = tag.Value?.ToString() ?? string.Empty;
                    break;
                }
            }

            captured.Add((measurement, checkName));
        });

        listener.Start();

        // Act ──────────────────────────────────────────────────────────────────
        await publisher.PublishAsync(report, CancellationToken.None);

        // ObservableGauge values are produced lazily; trigger the callback now.
        listener.RecordObservableInstruments();

        // Assert ───────────────────────────────────────────────────────────────
        var measurement = Assert.Single(
            captured.Where(m => m.CheckName == "check-under-test"));

        Assert.Equal(expectedValue, measurement.Value);
    }

    [Fact(DisplayName = "Should_EmitMeasurement_For_EachCheckInReport")]
    public async Task Should_EmitMeasurement_For_EachCheckInReport()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "ServiceDefaults.Tests"
        });

        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();

        var publisher = app.Services.GetServices<IHealthCheckPublisher>().Single();

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["check-a"] = MakeEntry(HealthStatus.Healthy),
                ["check-b"] = MakeEntry(HealthStatus.Degraded),
                ["check-c"] = MakeEntry(HealthStatus.Unhealthy)
            },
            TimeSpan.Zero);

        var capturedNames = new List<string>();
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "UserOrg.HealthChecks")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<int>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "check.name")
                {
                    capturedNames.Add(tag.Value?.ToString() ?? string.Empty);
                }
            }
        });

        listener.Start();

        // Act
        await publisher.PublishAsync(report, CancellationToken.None);
        listener.RecordObservableInstruments();

        // Assert: all three checks appear
        Assert.Contains("check-a", capturedNames);
        Assert.Contains("check-b", capturedNames);
        Assert.Contains("check-c", capturedNames);
    }

    [Fact(DisplayName = "Should_EmitNoMeasurements_Before_FirstPublish")]
    public async Task Should_EmitNoMeasurements_Before_FirstPublish()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "ServiceDefaults.Tests"
        });

        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();

        var captured = new List<int>();
        using var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "UserOrg.HealthChecks")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<int>((_, value, _, _) => captured.Add(value));
        listener.Start();

        // Act: record without publishing first
        listener.RecordObservableInstruments();

        // Assert: no report has been published yet, so no measurements
        Assert.Empty(captured);
    }

    private static HealthReport BuildReport(string checkName, HealthStatus status) =>
        new(
            new Dictionary<string, HealthReportEntry>
            {
                [checkName] = MakeEntry(status)
            },
            TimeSpan.Zero);

    private static HealthReportEntry MakeEntry(HealthStatus status) =>
        new(status, description: null, TimeSpan.Zero, exception: null, data: null);
}
