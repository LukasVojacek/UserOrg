using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;
using Waldur.Sync.Diff;
using Waldur.Sync.Mapping;
using Waldur.Sync.Tests.Mapping;
using Waldur.Sync.Worker;

namespace Waldur.Sync.Tests.Worker;

/// <summary>
/// Assembles a <see cref="WaldurSyncWorker"/> over the fake client/publisher/rebuilder for a
/// full happy-path iteration: one customer (with an offering) → one provider, one project (with a
/// resource on that offering) → one project, one project user → one membership.
/// </summary>
internal sealed class WorkerTestScenario
{
    public FakeWaldurApiClient Client { get; } = new();
    public FakeWaldurEventPublisher Publisher { get; } = new();
    public FakeShaMapRebuilder Rebuilder { get; set; } = new();
    public RecordingTimeProvider Time { get; } = new();
    public ILogger<WaldurSyncWorker> Logger { get; set; } = NullLogger<WaldurSyncWorker>.Instance;
    public WaldurInstanceOptions Options { get; }

    public WorkerTestScenario(
        WaldurStorageOptions? storage = null,
        int syncIntervalMinutes = 30,
        IDictionary<string, WaldurHeappeInstanceOptions>? heappe = null)
    {
        Options = MapperTestData.Options(heappe: heappe, storage: storage);
        Options.SyncIntervalMinutes = syncIntervalMinutes;
    }

    public WorkerTestScenario WithFullHappyPath()
    {
        var offering = MapperTestData.Offering();
        Client.Customers = [MapperTestData.Customer()];
        Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [MapperTestData.CustomerId] = [offering]
        };
        Client.Projects = [MapperTestData.Project()];
        Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };
        Client.UsersByProject = new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.User()]
        };
        return this;
    }

    public WaldurSyncWorker Build() =>
        new(
            Options,
            Client,
            new WaldurMapper(Options, Time),
            new DiffEngine(Options.Name, Options.Identity.ServiceName!),
            Publisher,
            Rebuilder,
            Logger,
            Time);
}
