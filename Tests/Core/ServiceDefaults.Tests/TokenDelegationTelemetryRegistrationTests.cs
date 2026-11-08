using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using TokenDelegation;

using UserOrg.ServiceDefaults;

namespace ServiceDefaults.Tests;

/// <summary>
/// The OpenTelemetry setup every UserOrg service shares collects the TokenDelegation meter and activity source
/// (ADR-0118, QS-06): the exchange failure meter is the kill switch trigger, so a service that exchanges tokens
/// must export it. An instrument is enabled, and a source has listeners, only when a provider subscribed to its
/// name; the outbox meter and source registered today prove the probe.
/// </summary>
public sealed class TokenDelegationTelemetryRegistrationTests
{
    private const string OutboxTelemetryName = "UserOrg.Core.Outbox";

    [Fact]
    public void The_meter_provider_collects_the_outbox_meter()
    {
        using var provider = ConfiguredProvider();
        _ = provider.GetRequiredService<MeterProvider>();

        Assert.True(InstrumentEnabled(OutboxTelemetryName));
    }

    [Fact]
    public void The_tracer_provider_listens_to_the_outbox_source()
    {
        using var provider = ConfiguredProvider();
        _ = provider.GetRequiredService<TracerProvider>();

        Assert.True(SourceHasListeners(OutboxTelemetryName));
    }

    [Fact]
    public void The_meter_provider_collects_the_token_delegation_meter()
    {
        using var provider = ConfiguredProvider();
        _ = provider.GetRequiredService<MeterProvider>();

        Assert.True(InstrumentEnabled(TokenDelegationTelemetry.MeterName));
    }

    [Fact]
    public void The_tracer_provider_listens_to_the_token_delegation_source()
    {
        using var provider = ConfiguredProvider();
        _ = provider.GetRequiredService<TracerProvider>();

        Assert.True(SourceHasListeners(TokenDelegationTelemetry.ActivitySourceName));
    }

    private static ServiceProvider ConfiguredProvider()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "UserOrg.Api"
        });
        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddReader(_ => new DiscardingMetricReader()));
        return builder.Services.BuildServiceProvider();
    }

    private static bool InstrumentEnabled(string meterName)
    {
        using var meter = new Meter(meterName);
        return meter.CreateCounter<long>("userorg.telemetry.registration.probe").Enabled;
    }

    private static bool SourceHasListeners(string sourceName)
    {
        using var source = new ActivitySource(sourceName);
        return source.HasListeners();
    }

    private sealed class DiscardingMetricReader : MetricReader;
}
