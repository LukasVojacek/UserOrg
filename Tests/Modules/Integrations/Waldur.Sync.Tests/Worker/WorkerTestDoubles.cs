using System.Collections.Concurrent;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Waldur.Contracts;
using Waldur.Contracts.Events;
using Waldur.Sync;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Diff;
using Waldur.Sync.Http;

namespace Waldur.Sync.Tests.Worker;

/// <summary>
/// A scripted <see cref="IWaldurApiClient"/>: returns fixed customers/offerings/projects/resources/
/// users per call, with optional per-endpoint throwing to exercise fetch-failure paths. Records the
/// number of iterations (via <see cref="GetCustomersAsync"/> calls) so a test can stop the loop.
/// </summary>
internal sealed class FakeWaldurApiClient : IWaldurApiClient
{
    public IReadOnlyList<ApiCustomer> Customers { get; set; } = [];
    public IReadOnlyDictionary<Guid, IReadOnlyList<ApiPublicOffering>> OfferingsByCustomer { get; set; } =
        new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>();
    public IReadOnlyList<ApiProject> Projects { get; set; } = [];
    public IReadOnlyDictionary<Guid, IReadOnlyList<ApiMarketplaceResource>> ResourcesByProject { get; set; } =
        new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>();
    public IReadOnlyDictionary<Guid, IReadOnlyList<ApiProjectUser>> UsersByProject { get; set; } =
        new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>();

    public HashSet<Guid> ProjectsThatFailUserFetch { get; } = [];
    public HashSet<Guid> CustomersThatFailOfferingFetch { get; } = [];
    public HashSet<Guid> ProjectsThatFailResourceFetch { get; } = [];
    public bool CustomersFetchThrows { get; set; }

    /// <summary>When set, the customer list fetch returns <see cref="Customers"/> flagged incomplete
    /// (a page failed) instead of complete — exercises the ListComplete gate (ADR-0064).</summary>
    public bool CustomerListIncomplete { get; set; }

    /// <summary>When set, the project list fetch returns <see cref="Projects"/> flagged incomplete.</summary>
    public bool ProjectListIncomplete { get; set; }

    /// <summary>When set, the per-project user fetch cancels this source and observes cancellation,
    /// so a test can assert the membership fetch re-throws <see cref="OperationCanceledException"/>.</summary>
    public CancellationTokenSource? CancelOnUserFetch { get; set; }

    public int CustomersCallCount { get; private set; }
    public Action? OnCustomersFetched { get; set; }

    public Task<PagedFetch<ApiCustomer>> GetCustomersAsync(CancellationToken ct)
    {
        CustomersCallCount++;
        OnCustomersFetched?.Invoke();
        ct.ThrowIfCancellationRequested();
        if (CustomersFetchThrows)
        {
            throw new Http.WaldurApiException("customers fetch failed");
        }

        return Task.FromResult(new PagedFetch<ApiCustomer>(Customers, IsComplete: !CustomerListIncomplete));
    }

    public Task<IReadOnlyList<ApiPublicOffering>> GetCustomerOfferingsAsync(Guid customerUuid, CancellationToken ct)
    {
        if (CustomersThatFailOfferingFetch.Contains(customerUuid))
        {
            throw new Http.WaldurApiException($"offerings fetch failed for {customerUuid}");
        }

        return Task.FromResult(OfferingsByCustomer.TryGetValue(customerUuid, out var o) ? o : []);
    }

    public Task<PagedFetch<ApiProject>> GetProjectsAsync(CancellationToken ct) =>
        Task.FromResult(new PagedFetch<ApiProject>(Projects, IsComplete: !ProjectListIncomplete));

    public Task<IReadOnlyList<ApiMarketplaceResource>> GetProjectResourcesAsync(Guid projectUuid, CancellationToken ct)
    {
        if (ProjectsThatFailResourceFetch.Contains(projectUuid))
        {
            throw new Http.WaldurApiException($"resources fetch failed for {projectUuid}");
        }

        return Task.FromResult(ResourcesByProject.TryGetValue(projectUuid, out var r) ? r : []);
    }

    public Task<ApiPublicOfferingDetail> GetOfferingDetailAsync(Guid offeringUuid, CancellationToken ct) =>
        Task.FromResult(new ApiPublicOfferingDetail(offeringUuid, "detail", "slug", null));

    /// <summary>Number of <see cref="GetProjectUsersAsync"/> calls — a test uses this to prove members
    /// were never fetched (as opposed to fetched-and-coincidentally-matching).</summary>
    public int UsersFetchCallCount { get; private set; }

    public Task<IReadOnlyList<ApiProjectUser>> GetProjectUsersAsync(Guid projectUuid, CancellationToken ct)
    {
        UsersFetchCallCount++;
        if (CancelOnUserFetch is not null)
        {
            CancelOnUserFetch.Cancel();
            ct.ThrowIfCancellationRequested();
        }

        if (ProjectsThatFailUserFetch.Contains(projectUuid))
        {
            throw new Http.WaldurApiException($"list_users failed for {projectUuid}");
        }

        return Task.FromResult(UsersByProject.TryGetValue(projectUuid, out var u) ? u : []);
    }
}

/// <summary>
/// A recording <see cref="IWaldurEventPublisher"/>. Captures every acked event in call order; can be
/// told to THROW (backstop path) for events matching <see cref="ThrowFor"/>, or to return a
/// <see cref="Result{TError}"/> FAILURE (expected-failure path, ADR-0065) for events matching
/// <see cref="FailFor"/> — per-item isolation + SHA-after-ack tests.
/// </summary>
internal sealed class FakeWaldurEventPublisher : IWaldurEventPublisher
{
    private readonly List<WaldurIntegrationEvent> _published = [];

    public IReadOnlyList<WaldurIntegrationEvent> Published => _published;

    public Func<WaldurIntegrationEvent, bool>? ThrowFor { get; set; }

    public Func<WaldurIntegrationEvent, bool>? FailFor { get; set; }

    public Action<WaldurIntegrationEvent>? OnPublished { get; set; }

    public Task<Result<Error>> PublishAsync(WaldurIntegrationEvent evt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowFor is not null && ThrowFor(evt))
        {
            throw new InvalidOperationException($"handler rejected {evt.GetType().Name}");
        }

        if (FailFor is not null && FailFor(evt))
        {
            // Expected dependency-missing failure: NOT acked, so it is not recorded as published.
            return Task.FromResult(Result<Error>.Failure(
                new Error("HandlerRejected", ErrorSeverity.Error, $"handler returned failure for {evt.GetType().Name}")));
        }

        _published.Add(evt);
        OnPublished?.Invoke(evt);
        return Task.FromResult(Result<Error>.Success());
    }
}

/// <summary>A cold-rebuild stub returning a fixed seed map.</summary>
internal sealed class FakeShaMapRebuilder : IShaMapRebuilder
{
    private readonly IReadOnlyDictionary<ShaKey, string> _seed;

    public FakeShaMapRebuilder(IReadOnlyDictionary<ShaKey, string>? seed = null) =>
        _seed = seed ?? new Dictionary<ShaKey, string>();

    public int CallCount { get; private set; }

    /// <summary>Number of leading calls that throw before the rebuild succeeds (transient-failure test).</summary>
    public int ThrowTimes { get; set; }

    public Task<IReadOnlyDictionary<ShaKey, string>> RebuildAsync(string instance, CancellationToken ct)
    {
        CallCount++;
        if (CallCount <= ThrowTimes)
        {
            throw new InvalidOperationException($"cold rebuild failed (attempt {CallCount})");
        }

        return Task.FromResult(_seed);
    }
}

/// <summary>
/// A <see cref="TimeProvider"/> whose <see cref="CreateTimer"/> records the requested delay and fires
/// the callback immediately, so the worker's inter-iteration <c>Task.Delay</c> completes at once and
/// the loop advances under test control. <see cref="OnDelay"/> runs when a delay is requested — a test
/// uses it to cancel the loop after N iterations.
/// </summary>
internal sealed class RecordingTimeProvider : TimeProvider
{
    public ConcurrentQueue<TimeSpan> Delays { get; } = new();

    public Action? OnDelay { get; set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Delays.Enqueue(dueTime);
        OnDelay?.Invoke();
        return new ImmediateTimer(callback, state);
    }

    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => callback(state));

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
