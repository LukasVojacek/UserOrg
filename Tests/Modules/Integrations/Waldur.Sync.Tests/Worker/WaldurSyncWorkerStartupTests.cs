using Microsoft.Extensions.Logging;

using Waldur.Contracts.Events;
using Waldur.Sync.Configuration;
using Waldur.Sync.Tests.TestSupport;
using Waldur.Sync.Worker;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerStartupTests
{
    private const int StartupStorageConfigRejectedEventId = 5005;

    private static WaldurStorageOptions EfpStorage() => new()
    {
        EfpCoreServiceName = "EFP-Core",
        UseSharedIrodsForAllEntities = false
    };

    [Fact]
    public async Task Should_ColdRebuild_Then_PublishOneStorageEvent_When_EfpStartup()
    {
        var scenario = new WorkerTestScenario(storage: EfpStorage()).WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);

        Assert.Equal(1, scenario.Rebuilder.CallCount);
        Assert.Single(scenario.Publisher.Published, e => e is StorageConfigurationChanged);
    }

    [Fact]
    public async Task Should_NotDoubleFireStorageEvent_When_FirstIterationRuns()
    {
        var scenario = new WorkerTestScenario(storage: EfpStorage()).WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // Startup recorded the config SHA on ack, so step 1's diff sees the slot unchanged: exactly one.
        Assert.Single(scenario.Publisher.Published, e => e is StorageConfigurationChanged);
    }

    [Fact]
    public async Task Should_ReEmitStorageEvent_When_StartupPublishFailed()
    {
        var scenario = new WorkerTestScenario(storage: EfpStorage()).WithFullHappyPath();
        scenario.Publisher.ThrowFor = e => e is StorageConfigurationChanged;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None); // startup publish fails → SHA not written

        scenario.Publisher.ThrowFor = null;
        await worker.RunIterationAsync(CancellationToken.None);

        // Startup swallowed the failure; step 1 re-diffs the unknown slot as Created → one event now.
        Assert.Single(scenario.Publisher.Published, e => e is StorageConfigurationChanged);
    }

    [Fact]
    public async Task Should_ReEmitStorageEvent_When_StartupPublishRejected()
    {
        // ADR-0065: the publisher REJECTS the startup config event via Result.Failure (the expected
        // dependency-missing path — the else branch / marker 5005), distinct from a thrown backstop.
        var logger = new CapturingLogger<WaldurSyncWorker>();
        var scenario = new WorkerTestScenario(storage: EfpStorage()) { Logger = logger }.WithFullHappyPath();
        scenario.Publisher.FailFor = e => e is StorageConfigurationChanged;
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None); // rejected → SHA not written for the config slot

        Assert.Contains(logger.Entries, e =>
            e.EventId.Id == StartupStorageConfigRejectedEventId && e.Level == LogLevel.Error);

        scenario.Publisher.FailFor = null;
        await worker.RunIterationAsync(CancellationToken.None);

        // No SHA was written on rejection, so step 1's diff re-emits the unknown slot as Created: exactly one.
        Assert.Single(scenario.Publisher.Published, e => e is StorageConfigurationChanged);
    }

    [Fact]
    public async Task Should_NeverPublishStorageEvent_When_NonEfpInstance()
    {
        var scenario = new WorkerTestScenario(storage: null).WithFullHappyPath();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        Assert.DoesNotContain(scenario.Publisher.Published, e => e is StorageConfigurationChanged);
    }
}
