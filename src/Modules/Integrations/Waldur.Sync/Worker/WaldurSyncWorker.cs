using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Waldur.Contracts;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;
using Waldur.Sync.Diff;
using Waldur.Sync.Http;
using Waldur.Sync.Mapping;

namespace Waldur.Sync.Worker;

/// <summary>
/// The sequential per-instance sync worker (ADR-0056). One instance runs one worker: on startup it
/// cold-rebuilds the SHA breadcrumb map and (EFP only) fires one unconditional
/// <c>StorageConfigurationChanged</c>; then it loops fetch → map → diff → publish, applying each SHA
/// mutation <b>only after</b> its event is acked (D5) and sleeping <c>SyncIntervalMinutes</c>
/// measured from iteration completion (A2). All cleverness lives in the already-tested pure parts
/// (mapper B03, diff B04); this class is deliberately boring plumbing + structured logging/OTel.
/// </summary>
public sealed class WaldurSyncWorker : BackgroundService
{
    private readonly WaldurInstanceOptions _options;
    private readonly IWaldurApiClient _client;
    private readonly WaldurMapper _mapper;
    private readonly DiffEngine _diffEngine;
    private readonly IWaldurEventPublisher _publisher;
    private readonly IShaMapRebuilder _rebuilder;
    private readonly ILogger<WaldurSyncWorker> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly string _instance;
    private readonly string _serviceName;
    private readonly bool _isEfp;

    private ShaBreadcrumbMap _map = new();
    private bool _started;

    public WaldurSyncWorker(
        WaldurInstanceOptions options,
        IWaldurApiClient client,
        WaldurMapper mapper,
        DiffEngine diffEngine,
        IWaldurEventPublisher publisher,
        IShaMapRebuilder rebuilder,
        ILogger<WaldurSyncWorker> logger,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _rebuilder = rebuilder ?? throw new ArgumentNullException(nameof(rebuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _instance = _options.Name;
        _serviceName = _options.Identity.ServiceName
            ?? throw new ArgumentException("Instance identity ServiceName is required.", nameof(options));
        _isEfp = _options.Storage is not null;
    }

    /// <summary>Current SHA breadcrumb-map size (feeds the OTel gauge + tests).</summary>
    internal int MapSize => _map.Count;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunAsync(stoppingToken);

    /// <summary>
    /// The loop: cold-rebuild once (retried on failure), then <c>{ iterate; sleep
    /// interval-from-completion }</c> until cancelled. A worker <b>contains its own failures</b> —
    /// a non-cancellation exception (incl. a failed cold rebuild) is logged and the worker backs off
    /// one interval and retries, so one instance's fault never propagates out of
    /// <see cref="BackgroundService.ExecuteAsync"/> and stops the whole host (and every other
    /// instance worker). Requested cancellation still unwinds cleanly.
    /// </summary>
    internal async Task RunAsync(CancellationToken ct)
    {
        WaldurSyncDiagnostics.RegisterMapSize(_serviceName, () => _map.Count);
        var interval = TimeSpan.FromMinutes(_options.SyncIntervalMinutes);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Startup runs once; a failed cold rebuild leaves _started false so we retry the
                    // rebuild next tick rather than iterating on a bad/empty map (which would
                    // re-publish everything).
                    if (!_started)
                    {
                        await StartupAsync(ct).ConfigureAwait(false);
                        _started = true;
                    }

                    await RunIterationAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // shutdown — unwind cleanly via the outer handler.
                }
                catch (Exception ex)
                {
                    // Contain to THIS worker: log and back off, do not fault the host.
                    WaldurSyncLog.WorkerFaulted(_logger, _instance, ex);
                }

                // A2: delay measured from iteration COMPLETION (not a fixed-rate timer), so an
                // iteration slower than the interval can never overlap the next one.
                await Task.Delay(interval, _timeProvider, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Requested stop — clean unwind, no error.
        }
        finally
        {
            WaldurSyncDiagnostics.UnregisterMapSize(_serviceName);
        }
    }

    /// <summary>
    /// Startup step 0 (ADR §Iteration algorithm): cold-rebuild the SHA map, then — EFP instances only —
    /// publish ONE unconditional <c>StorageConfigurationChanged</c> and record its SHA on ack, so
    /// step 1's diff sees the config slot unchanged and does not double-fire the same event.
    /// </summary>
    internal async Task StartupAsync(CancellationToken ct)
    {
        WaldurSyncLog.StartupRebuilding(_logger, _instance);
        var seed = await _rebuilder.RebuildAsync(_instance, ct).ConfigureAwait(false);
        _map = new ShaBreadcrumbMap(seed);
        WaldurSyncLog.StartupRebuilt(_logger, _instance, _map.Count);

        if (!_isEfp)
        {
            return;
        }

        var config = _mapper.MapStorageConfig();
        var evt = new StorageConfigurationChanged(config) { Instance = _instance, ServiceName = _serviceName };
        try
        {
            var pub = await _publisher.PublishAsync(evt, ct).ConfigureAwait(false);
            if (pub.IsSuccess)
            {
                _map.Apply(ShaMutation.Set(ShaKey.StorageConfig(), config.ComputeIdentityHash()));
                WaldurSyncLog.StartupStorageConfigPublished(_logger, _instance);
            }
            else
            {
                // Expected dependency-missing failure (ADR-0065): no SHA write → step 1's diff re-emits
                // it (unknown slot → Created); the consumer is idempotent, so a retried publish is harmless.
                WaldurSyncLog.StartupStorageConfigRejected(_logger, _instance, pub.Error.Description);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // BACKSTOP: an unexpected fault. No SHA write → step 1's diff re-emits it (unknown slot →
            // Created); the consumer is idempotent, so a retried publish is harmless.
            WaldurSyncLog.StartupStorageConfigFailed(_logger, _instance, ex);
        }
    }

    /// <summary>Runs exactly one iteration (steps 1–6) and returns its always-log summary.</summary>
    internal async Task<IterationSummary> RunIterationAsync(CancellationToken ct)
    {
        using var activity = WaldurSyncDiagnostics.ActivitySource.StartActivity(
            WaldurSyncDiagnostics.IterationActivityName);
        activity?.SetTag(WaldurSyncDiagnostics.InstanceTag, _instance);

        WaldurSyncLog.IterationStarting(_logger, _instance, "interval");
        var startedAt = _timeProvider.GetTimestamp();

        DiffInput input;
        try
        {
            input = await BuildInputAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A fetch that faults during shutdown is not a real phase failure — surface it as clean
            // cancellation instead of a spurious Error log.
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            // A whole-phase fetch failure (Waldur API down) logs ONCE and abandons the iteration;
            // nothing is acked, so everything is re-diffed next iteration.
            WaldurSyncLog.PhaseFailed(_logger, _instance, WaldurSyncPhase.Fetch, ex);
            var elapsed = _timeProvider.GetElapsedTime(startedAt);
            var failedSummary = new IterationSummary(0, 0, 0, 0, 0, 0, 0, 0) { Duration = elapsed, FetchFailed = true };
            LogSummary(failedSummary);
            RecordMetrics(elapsed);
            return failedSummary;
        }

        var result = _diffEngine.Diff(input, _map);
        var summary = await ApplyActionsAsync(input, result, ct).ConfigureAwait(false);

        var duration = _timeProvider.GetElapsedTime(startedAt);
        summary = summary with { Duration = duration };
        LogSummary(summary);
        RecordMetrics(duration);
        return summary;
    }

    private async Task<IterationSummary> ApplyActionsAsync(DiffInput input, DiffResult result, CancellationToken ct)
    {
        int created = 0, updated = 0, removed = 0, failed = 0, blockedByParent = 0, entityActions = 0;
        var failedParents = new HashSet<string>(StringComparer.Ordinal);

        foreach (var action in result.Actions)
        {
            var descriptor = EventDescriptor.For(action.Event);
            if (descriptor.CountsAsFetchedEntity)
            {
                entityActions++;
            }

            // ADR-0066: so an operator greps the remote id and reconstructs the whole entity subtree.
            // Disposed per item.
            using var span = WaldurSyncDiagnostics.StartEntitySpan(
                SpanNameFor(descriptor.EntityType), _instance, descriptor.EntityType, descriptor.ExternalId);
            using var scope = BeginCorrelationScope(descriptor.ExternalId);

            WaldurSyncLog.EntityProcessing(_logger, _instance, descriptor.EntityType, descriptor.ExternalId);
            WaldurSyncLog.EventEmitting(_logger, _instance, action.Event.GetType().Name, descriptor.EntityType, descriptor.ExternalId);

            try
            {
                var pub = await _publisher.PublishAsync(action.Event, ct).ConfigureAwait(false);
                if (pub.IsSuccess)
                {
                    _map.Apply(action.OnAck); // D5: SHA written ONLY after a successful ack.

                    switch (descriptor.Change)
                    {
                        case ChangeKind.Created:
                        created++;
                        break;
                        case ChangeKind.Updated:
                        updated++;
                        break;
                        case ChangeKind.Removed:
                        removed++;
                        break;
                    }

                    WaldurSyncLog.EntityProcessed(_logger, _instance, descriptor.EntityType, descriptor.ExternalId);
                }
                else
                {
                    // ADR-0065: an EXPECTED dependency-missing failure — no SHA write, no ack, the item
                    // re-diffs next iteration. Logged with the handler's structured Error (not an exception).
                    RecordFailure(descriptor, failedParents, ref failed, ref blockedByParent);
                    WaldurSyncLog.ItemRejected(_logger, _instance, descriptor.EntityType, descriptor.ExternalId,
                        WaldurSyncPhase.Handler, pub.Error.Description);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // BACKSTOP (ADR-0065): an UNEXPECTED fault (infrastructure/programming error). Per-item
                // isolation — no SHA write, continue with the next item (retried next iteration).
                RecordFailure(descriptor, failedParents, ref failed, ref blockedByParent);
                WaldurSyncLog.ItemFailed(_logger, _instance, descriptor.EntityType, descriptor.ExternalId,
                    WaldurSyncPhase.Handler, ex);
            }
        }

        var absent = ApplyAbsences(result);
        var fetched = CountFetched(input);
        var unchanged = Math.Max(0, fetched - entityActions);

        return new IterationSummary(fetched, created, updated, removed, unchanged, failed, blockedByParent, absent);
    }

    /// <summary>
    /// Shared no-ack bookkeeping for both failure paths (expected Result.Failure and backstopped
    /// exception): increment <paramref name="failed"/>, record the blocked-by-parent cascade
    /// (ADR-0058/A2), and remember this entity's id so its later children can detect the cascade.
    /// </summary>
    private static void RecordFailure(
        EventDescriptor descriptor, HashSet<string> failedParents, ref int failed, ref int blockedByParent)
    {
        failed++;
        if (descriptor.ParentExternalId is { } parent && failedParents.Contains(parent))
        {
            blockedByParent++;
        }

        failedParents.Add(descriptor.ExternalId);
    }

    private int ApplyAbsences(DiffResult result)
    {
        foreach (var absence in result.Absences)
        {
            WaldurSyncLog.AbsenceTransition(_logger, _instance, absence.Key.EntityType, absence.Key.ExternalId);
            _map.Remove(absence.Key); // Transition-only warning (ADR-0058/A1): never re-fires.
        }

        return result.Absences.Count;
    }

    private async Task<DiffInput> BuildInputAsync(CancellationToken ct)
    {
        var providers = await BuildProvidersAsync(ct).ConfigureAwait(false);
        var projects = await BuildProjectsAsync(providers.OfferingsById, providers.FailedOfferingCustomers, ct)
            .ConfigureAwait(false);
        var storageConfig = _isEfp ? _mapper.MapStorageConfig() : null;

        return new DiffInput
        {
            Providers = providers.Inputs,
            Projects = projects.Inputs,
            StorageConfig = storageConfig,
            ProvidersComplete = providers.IsComplete,
            ProjectsComplete = projects.IsComplete
        };
    }

    private async Task<(IReadOnlyList<ProviderDiffInput> Inputs,
        IReadOnlyDictionary<Guid, ApiPublicOffering> OfferingsById,
        IReadOnlySet<Guid> FailedOfferingCustomers,
        bool IsComplete)>
        BuildProvidersAsync(CancellationToken ct)
    {
        var customerFetch = await _client.GetCustomersAsync(ct).ConfigureAwait(false);
        var customers = customerFetch.Items;

        // ADR-0064: a partial customer list (a page failed) is carried forward as-is — its present
        // customers still upsert — but the provider absence sweep is suppressed downstream so a
        // customer merely on the un-fetched page is not swept to absence.
        if (!customerFetch.IsComplete)
        {
            LogFetchFailure(WaldurSyncDiagnostics.ProjectActivityName, WaldurEntityType.Provider, "customer-list",
                ListIncompleteReason("customer"), exception: null);
        }

        // Bounded fan-out over the per-customer offering fetches (API-latency hot spot, ADR-0056/A4).
        // Each lambda goes through the Result seam (TryFetchAsync): a failed offering page becomes a
        // failed Result instead of throwing, so BoundedFanOut never fast-fails a healthy sibling and
        // that one customer is carried forward this iteration (ADR-0062).
        var offeringResults = await BoundedFanOut.MapAsync(
            customers,
            _options.MaxFanOutConcurrency,
            (customer, token) => TryFetchAsync(
                WaldurEntityType.Provider,
                ToExternalId(customer.Id),
                t => _client.GetCustomerOfferingsAsync(customer.Id, t),
                token),
            ct).ConfigureAwait(false);

        var offeringsById = new Dictionary<Guid, ApiPublicOffering>();
        var failedOfferingCustomers = new HashSet<Guid>();
        for (var i = 0; i < offeringResults.Count; i++)
        {
            if (!offeringResults[i].IsSuccess)
            {
                failedOfferingCustomers.Add(customers[i].Id);
                continue;
            }

            foreach (var offering in offeringResults[i].Value)
            {
                offeringsById[offering.Id] = offering; // full offering — the mapper's per-resource source.
            }
        }

        var inputs = new List<ProviderDiffInput>(customers.Count);
        for (var i = 0; i < customers.Count; i++)
        {
            var customer = customers[i];
            if (failedOfferingCustomers.Contains(customer.Id))
            {
                // ADR-0062: so DetectAbsences never sees it as absent and its breadcrumb survives —
                // never omit it from the input list.
                inputs.Add(ProviderDiffInput.CarriedForward(ToExternalId(customer.Id)));
                continue;
            }

            var result = _mapper.MapProvider(customer, offeringResults[i].Value);
            LogSkips(result.Skips);
            inputs.Add(new ProviderDiffInput(ToExternalId(customer.Id), result.Payload));
        }

        return (inputs, offeringsById, failedOfferingCustomers, customerFetch.IsComplete);
    }

    private async Task<(IReadOnlyList<ProjectDiffInput> Inputs, bool IsComplete)> BuildProjectsAsync(
        IReadOnlyDictionary<Guid, ApiPublicOffering> offeringsById,
        IReadOnlySet<Guid> failedOfferingCustomers,
        CancellationToken ct)
    {
        var projectFetch = await _client.GetProjectsAsync(ct).ConfigureAwait(false);
        var projects = projectFetch.Items;

        // ADR-0064: a partial project list (a page failed) is carried forward as-is — its present
        // projects still upsert — but the project absence sweep is suppressed downstream so a project
        // merely on the un-fetched page is not swept to absence.
        if (!projectFetch.IsComplete)
        {
            LogFetchFailure(WaldurSyncDiagnostics.ProjectActivityName, WaldurEntityType.Project, "project-list",
                ListIncompleteReason("project"), exception: null);
        }

        // Bounded fan-out over per-project resource fetches through the Result seam (TryFetchAsync): a
        // failed resource page becomes a failed Result instead of throwing, so BoundedFanOut never
        // fast-fails a healthy sibling and that one project is carried forward (ADR-0062).
        var resourceResults = await BoundedFanOut.MapAsync(
            projects,
            _options.MaxFanOutConcurrency,
            (project, token) => TryFetchAsync(
                WaldurEntityType.Project,
                ToExternalId(project.Id),
                t => _client.GetProjectResourcesAsync(project.Id, t),
                token),
            ct).ConfigureAwait(false);

        var anyOfferingFetchFailed = failedOfferingCustomers.Count > 0;

        var inputs = new List<ProjectDiffInput>(projects.Count);
        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            var resourceFetchFailed = !resourceResults[i].IsSuccess;

            ProjectPayload? payload = null;
            if (!resourceFetchFailed)
            {
                var resourceOfferings = new List<ResourceOffering>(resourceResults[i].Value.Count);
                foreach (var resource in resourceResults[i].Value)
                {
                    var hasOffering = offeringsById.TryGetValue(resource.OfferingId, out var offering);

                    if (!hasOffering && anyOfferingFetchFailed)
                    {
                        LogResourceOfferingSuspect(ToExternalId(resource.Id));
                    }

                    resourceOfferings.Add(new ResourceOffering(resource, hasOffering ? offering : null));
                }

                var mapped = _mapper.MapProject(project, resourceOfferings.ToImmutableList());
                LogSkips(mapped.Skips);
                payload = mapped.Payload;
            }

            // ADR-0062: a resource-fetch failure carries the project forward (null payload) so its
            // breadcrumb survives; members do not depend on resources (D12/ADR-0063), so a non-removed
            // carried-forward project still has its membership fetched and emitted.
            var membership = await FetchMembershipAsync(project, payload, resourceFetchFailed, anyOfferingFetchFailed, ct)
                .ConfigureAwait(false);
            inputs.Add(new ProjectDiffInput(ToExternalId(project.Id), payload, membership));
        }

        return (inputs, projectFetch.IsComplete);
    }

    private async Task<MembershipSnapshot?> FetchMembershipAsync(
        ApiProject project, ProjectPayload? payload, bool resourceFetchFailed, bool anyOfferingFetchFailed, CancellationToken ct)
    {
        // Users are fetched for eligible, non-removed projects (removed projects keep their memberships
        // in place — ADR-0054/A1). A resource-fetch failure carries the project forward with a null
        // payload, but members do not depend on resources (D12/ADR-0063), so a non-removed
        // carried-forward project still has its members fetched.
        //
        // ADR-0063/D12: a project emptied to a null payload because a resource's offering was dropped
        // when that offering's CUSTOMER fetch failed (offering→customer skip) is ALSO a transient
        // failure carry-forward — members must keep syncing. The resource carries no customer ref, so
        // attribution is at iteration granularity (B04): any offering-fetch failure this iteration makes
        // such a carry-forward suspect, so we keep fetching members. A project genuinely, permanently
        // ineligible during an iteration with NO offering failure stays members-skipped (legacy parity).
        var fetchMembers = payload is { IsRemoved: false }
            || (!project.IsRemoved && (resourceFetchFailed || anyOfferingFetchFailed));
        if (!fetchMembers)
        {
            return null;
        }

        var fetch = await TryFetchAsync(
            WaldurEntityType.Project,
            ToExternalId(project.Id),
            token => _client.GetProjectUsersAsync(project.Id, token),
            ct);

        // D7: a failed member fetch yields NO membership events and leaves member SHAs untouched.
        if (!fetch.IsSuccess)
        {
            return MembershipSnapshot.Failed();
        }

        var mapped = _mapper.MapMembership(project.Id, fetch.Value);
        LogSkips(mapped.Skips);

        // The client throws on partial pagination, so a successful return is complete by construction.
        return MembershipSnapshot.Complete(mapped.Members);
    }

    /// <summary>
    /// The <b>Result seam</b> at the producer fetch boundary (ADR-0062/0065, B01): runs a per-item
    /// detail fetch and turns an <b>expected</b> failure into a <see cref="Result{TValue,TError}"/>
    /// failure — logged once at Error — instead of letting it throw. Because the mapped fan-out lambda
    /// no longer throws, <see cref="BoundedFanOut"/> never fast-fails a healthy sibling (ADR-0062);
    /// the caller carries the failed item forward. Requested cancellation is re-thrown so shutdown
    /// still unwinds cleanly.
    /// </summary>
    private async Task<Result<T, Error>> TryFetchAsync<T>(
        string entityType,
        string externalId,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken ct)
    {
        try
        {
            return Result<T, Error>.Success(await fetch(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ADR-0066: a root detail-fetch failure logs the dedicated Error marker inside a per-item
            // span/scope so its line carries TraceId + SpanId + ExternalId (greppable by remote id).
            LogFetchFailure(SpanNameFor(entityType), entityType, externalId, "detail fetch faulted", ex);
            return Result<T, Error>.Failure(WaldurSyncErrors.FetchFailed(entityType, externalId));
        }
    }

    private static int CountFetched(DiffInput input)
    {
        var providers = input.Providers.Count(p => p.Payload is not null);
        var projects = input.Projects.Count(p => p.Payload is not null);
        var members = input.Projects.Sum(p => p.Membership is { IsComplete: true } s ? s.Members.Count : 0);
        return providers + projects + members;
    }

    private void LogSkips(IReadOnlyList<MappingSkip> skips)
    {
        foreach (var skip in skips)
        {
            WaldurSyncLog.MappingSkipped(_logger, _instance, skip.EntityType, skip.ExternalId,
                skip.Reason.ToString(), skip.Detail);
        }
    }

    private void LogSummary(IterationSummary summary) =>
        WaldurSyncLog.IterationCompleted(_logger, _instance, summary.Duration.TotalMilliseconds,
            summary.Fetched, summary.Created, summary.Updated, summary.Removed, summary.Unchanged,
            summary.Failed, summary.BlockedByParent, summary.Absent);

    private void RecordMetrics(TimeSpan duration) =>
        WaldurSyncDiagnostics.RecordIteration(_serviceName, duration);

    private static string ToExternalId(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    // ADR-0064: the resilient list fetch swallows the failing page and only signals incompleteness,
    // so the worker supplies the Error-log reason.
    private static string ListIncompleteReason(string listName) =>
        $"{listName} list fetch was incomplete (a page failed); absence sweep suppressed this iteration (ADR-0064)";

    // ── ADR-0066 tracing: per-entity child span + log correlation scope ─────────

    private static string SpanNameFor(string entityType) => entityType == WaldurEntityType.Member
        ? WaldurSyncDiagnostics.UserActivityName
        : WaldurSyncDiagnostics.ProjectActivityName;

    /// <summary>
    /// Debug marker for a resource whose offering is missing while an offering fetch failed this
    /// iteration. Gated on Debug so the span + scope allocations are skipped when Debug is off.
    /// </summary>
    private void LogResourceOfferingSuspect(string resourceExternalId)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        // ADR-0063 (B04): a resource whose offering is missing *because an upstream offering fetch
        // failed this iteration* is a transient dependency skip — logged at Debug to distinguish it
        // from a genuine ResourceOfferingMissing (the mapper's Warning). The resource carries only
        // offering_uuid (no customer ref), so attribution is at iteration granularity: any
        // offering-fetch failure makes a missing offering suspect.
        using var span = WaldurSyncDiagnostics.StartEntitySpan(
            WaldurSyncDiagnostics.ResourceActivityName, _instance, WaldurSyncDiagnostics.ResourceEntityType, resourceExternalId);
        using var scope = BeginCorrelationScope(resourceExternalId);
        WaldurSyncLog.ResourceSkippedByFetchFailure(_logger, _instance, resourceExternalId);
    }

    /// <summary>
    /// Stamps every log line written inside the scope with the current TraceId + SpanId + the entity's
    /// remote id (ADR-0066), so the existing structured markers inherit the correlation without changing
    /// their signatures. Reads <see cref="Activity.Current"/> (the entity span opened just before).
    /// </summary>
    private IDisposable? BeginCorrelationScope(string externalId)
    {
        var activity = Activity.Current;
        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = activity?.TraceId.ToString() ?? string.Empty,
            ["SpanId"] = activity?.SpanId.ToString() ?? string.Empty,
            ["ExternalId"] = externalId,
        });
    }

    /// <summary>
    /// Logs the dedicated root fetch/dependency failure Error marker (ADR-0066) inside a per-item
    /// span/scope so its line is greppable by remote id and correlated by TraceId/SpanId.
    /// </summary>
    private void LogFetchFailure(string activityName, string entityType, string externalId, string reason, Exception? exception)
    {
        using var span = WaldurSyncDiagnostics.StartEntitySpan(activityName, _instance, entityType, externalId);
        using var scope = BeginCorrelationScope(externalId);
        WaldurSyncLog.FetchDependencyFailed(
            _logger, _instance, entityType, externalId, reason, WaldurSyncPhase.Fetch, exception);
    }

    private enum ChangeKind
    {
        Created,
        Updated,
        Removed
    }

    /// <summary>
    /// Per-event logging identity + summary classification + parent linkage (for the
    /// blocked-by-parent cascade count, ADR-0058/A2). Membership's parent is its project (the only
    /// cleanly derivable cascade — a failed project makes its member events blocked-by-parent).
    /// </summary>
    private readonly record struct EventDescriptor(
        string EntityType,
        string ExternalId,
        ChangeKind Change,
        bool CountsAsFetchedEntity,
        string? ParentExternalId)
    {
        public static EventDescriptor For(WaldurIntegrationEvent evt) => evt switch
        {
            ProviderCreated p => new(WaldurEntityType.Provider, p.Payload.ExternalId, ChangeKind.Created, true, null),
            ProviderUpdated p => new(WaldurEntityType.Provider, p.Payload.ExternalId, ChangeKind.Updated, true, null),
            ProjectCreated p => new(WaldurEntityType.Project, p.Payload.ExternalId, ChangeKind.Created, true, null),
            ProjectUpdated p => new(WaldurEntityType.Project, p.Payload.ExternalId, ChangeKind.Updated, true, null),
            UserAssigned u => new(WaldurEntityType.Member, MemberId(u.Payload), ChangeKind.Created, true, u.Payload.ProjectExternalId),
            UserRoleChanged u => new(WaldurEntityType.Member, MemberId(u.Payload), ChangeKind.Updated, true, u.Payload.ProjectExternalId),
            UserRemoved u => new(WaldurEntityType.Member, $"{u.ProjectExternalId}/{u.UserName}", ChangeKind.Removed, false, u.ProjectExternalId),
            StorageConfigurationChanged => new(WaldurEntityType.StorageConfig, ShaKey.StorageConfigSlot, ChangeKind.Updated, false, null),
            _ => new(evt.GetType().Name, "?", ChangeKind.Updated, false, null)
        };

        private static string MemberId(ProjectUserPayload payload) => $"{payload.ProjectExternalId}/{payload.UserName}";
    }
}