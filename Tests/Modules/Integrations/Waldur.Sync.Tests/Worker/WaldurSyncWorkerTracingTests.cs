using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Tests.Mapping;
using Waldur.Sync.Tests.TestSupport;
using Waldur.Sync.Worker;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

/// <summary>
/// ADR-0066: every sync log line is greppable by remote id and correlated by TraceId (per iteration)
/// + SpanId (per entity), log-only. The iteration Activity is the trace root, each inner unit opens a
/// child span, and each log site is stamped via <c>BeginScope</c> with TraceId + SpanId + ExternalId.
/// A root fetch/dependency failure is Error (dedicated marker <c>5035</c>); a proactively skipped
/// dependent is Debug (<c>5033</c>).
/// </summary>
public class WaldurSyncWorkerTracingTests
{
    private const int FetchDependencyFailedEventId = 5035;
    private const int ResourceSkippedByFetchFailureEventId = 5033;

    private static readonly Guid CustomerA = MapperTestData.CustomerId;

    /// <summary>
    /// Without an OTel listener <see cref="ActivitySource.StartActivity(string)"/> returns null, so the
    /// test installs a listener for the <c>Waldur.Sync</c> source (Sample = AllData) to force real
    /// Activities — mirroring the production <c>ParentBased(AlwaysOn)</c> sampler.
    /// </summary>
    private static ActivityListener ListenTo(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Should_ShareIterationTraceId_And_DistinctSpanId_PerEntity()
    {
        using var listener = ListenTo(WaldurSyncDiagnostics.SourceName);

        var logger = new CapturingLogger<WaldurSyncWorker>();
        var scenario = new WorkerTestScenario { Logger = logger };

        // The customer's offering fetch fails (root fetch failure → Error), so its offering never lands
        // in offeringsById; the project's resource references that now-missing offering (dependent skip
        // → Debug).
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerA);
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        var error = Assert.Single(logger.Entries, e => e.EventId.Id == FetchDependencyFailedEventId);
        var debug = Assert.Single(logger.Entries, e => e.EventId.Id == ResourceSkippedByFetchFailureEventId);

        // Both entities share the iteration TraceId (same trace root) …
        var errorTrace = error.ScopeValue("TraceId");
        var debugTrace = debug.ScopeValue("TraceId");
        Assert.False(string.IsNullOrEmpty(errorTrace));
        Assert.Equal(errorTrace, debugTrace);

        // … but each entity has its own SpanId (a distinct child span).
        var errorSpan = error.ScopeValue("SpanId");
        var debugSpan = debug.ScopeValue("SpanId");
        Assert.False(string.IsNullOrEmpty(errorSpan));
        Assert.False(string.IsNullOrEmpty(debugSpan));
        Assert.NotEqual(errorSpan, debugSpan);

        // … and each carries its own remote id, so an operator can grep by it.
        Assert.Equal(CustomerA.ToString("D"), error.ScopeValue("ExternalId"));
        Assert.Equal(MapperTestData.ResourceId.ToString("D"), debug.ScopeValue("ExternalId"));
    }

    [Fact]
    public async Task Should_UseErrorMarker_When_FetchFails()
    {
        using var listener = ListenTo(WaldurSyncDiagnostics.SourceName);

        var logger = new CapturingLogger<WaldurSyncWorker>();
        var scenario = new WorkerTestScenario { Logger = logger };
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerA);

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        var error = Assert.Single(logger.Entries, e => e.EventId.Id == FetchDependencyFailedEventId);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Equal(CustomerA.ToString("D"), error.ScopeValue("ExternalId"));
    }

    [Fact]
    public async Task Should_UseDebugMarker_When_DependentSkipped()
    {
        using var listener = ListenTo(WaldurSyncDiagnostics.SourceName);

        var logger = new CapturingLogger<WaldurSyncWorker>();
        var scenario = new WorkerTestScenario { Logger = logger };
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerA);
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        var debug = Assert.Single(logger.Entries, e => e.EventId.Id == ResourceSkippedByFetchFailureEventId);
        Assert.Equal(LogLevel.Debug, debug.Level);
    }
}
