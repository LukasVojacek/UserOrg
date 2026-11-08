using UserOrg.Shared.Hashing;

using Waldur.Contracts.Events;

namespace Waldur.Sync.Diff;

/// <summary>
/// The pure change-detection core of the sync (ADR-0054/0055). Given one iteration's mapped payloads
/// and the current SHA breadcrumb map, it decides which events to publish and which map mutations to
/// apply on ack — with <b>no I/O, no DB, no clock, and no mutation of the passed map</b>. One engine
/// per instance carries that instance's event identity.
///
/// Rules: unknown id → <c>Created</c>; hash differs → <c>Updated</c>; equal → nothing. Membership is
/// diffed per <c>(project, userName)</c> and only from a complete snapshot (D7). Ineligible projects
/// (null payload — ADR-0054/A4) and removed projects (ADR-0054/A1) produce no membership events.
/// Provider/project entries in the map but absent from the input are reported as absence transitions
/// (D3: no delete event).
/// </summary>
public sealed class DiffEngine
{
    private readonly string _instance;
    private readonly string _serviceName;

    /// <param name="instance">Instance name / OTel tag ("EFP", "Puhuri", …).</param>
    /// <param name="serviceName">Upsert-key discriminator ("EFP-Waldur", …).</param>
    public DiffEngine(string instance, string serviceName)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
    }

    /// <summary>Computes the events + deferred mutations + absence transitions for one iteration.</summary>
    public DiffResult Diff(DiffInput input, ShaBreadcrumbMap map)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(map);

        var actions = new List<DiffAction>();

        DiffProviders(input, map, actions);
        DiffProjects(input, map, actions);
        DiffStorageConfig(input, map, actions);

        var absences = DetectAbsences(input, map);

        return new DiffResult { Actions = actions, Absences = absences };
    }

    private void DiffProviders(DiffInput input, ShaBreadcrumbMap map, List<DiffAction> actions)
    {
        foreach (var provider in input.Providers)
        {
            if (provider.Payload is not { } payload)
            {
                continue;
            }

            var key = ShaKey.Provider(payload.ExternalId);
            EmitUpsert(map, actions, key, payload,
                p => new ProviderCreated(p) { Instance = _instance, ServiceName = _serviceName },
                p => new ProviderUpdated(p) { Instance = _instance, ServiceName = _serviceName });
        }
    }

    private void DiffProjects(DiffInput input, ShaBreadcrumbMap map, List<DiffAction> actions)
    {
        foreach (var project in input.Projects)
        {
            if (project.Payload is { } payload)
            {
                var key = ShaKey.Project(payload.ExternalId);
                EmitUpsert(map, actions, key, payload,
                    p => new ProjectCreated(p) { Instance = _instance, ServiceName = _serviceName },
                    p => new ProjectUpdated(p) { Instance = _instance, ServiceName = _serviceName });

                // Removed projects leave memberships in place — no membership events (ADR-0054/A1).
                if (payload.IsRemoved)
                {
                    continue;
                }
            }

            // Membership is diffed from a complete snapshot independently of the project payload: a
            // resource-fetch-failed project is carried forward with a null payload (ADR-0062) but its
            // members still flow (D12/ADR-0063). An ineligible project carries a null snapshot, so this
            // no-ops for it — no membership events, matching ADR-0054/A4.
            DiffMembership(project.ExternalId, project.Membership, map, actions);
        }
    }

    private void DiffMembership(
        string projectExternalId,
        MembershipSnapshot? snapshot,
        ShaBreadcrumbMap map,
        List<DiffAction> actions)
    {
        // D7: only a successfully fetched, complete snapshot may drive membership events (incl. removal).
        if (snapshot is not { IsComplete: true } complete)
        {
            return;
        }

        // Defensively dedup by UserName (first-wins) so the engine never relies on a producer (B03)
        // uniqueness guarantee: a duplicate username would otherwise double-emit and make the final
        // SHA depend on input order (EmitUpsert reads only the persisted map). The same set drives
        // the removal sweep, so a present duplicate cannot be seen as removed.
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in complete.Members)
        {
            if (!present.Add(member.UserName))
            {
                continue;
            }

            var key = ShaKey.Member(projectExternalId, member.UserName);
            EmitUpsert(map, actions, key, member,
                m => new UserAssigned(m) { Instance = _instance, ServiceName = _serviceName },
                m => new UserRoleChanged(m) { Instance = _instance, ServiceName = _serviceName });
        }

        // A user known to the map but missing from this complete snapshot is an explicit removal.
        // Perf note: this is an O(projects x mapKeys) scan; if profiling ever shows it matters,
        // build a ToLookup(by project id) over map.Keys once per iteration instead.
        foreach (var key in map.Keys)
        {
            if (key.EntityType == WaldurEntityType.Member
                && key.ExternalId == projectExternalId
                && key.UserName is { } userName
                && !present.Contains(userName))
            {
                actions.Add(new DiffAction(
                    new UserRemoved(projectExternalId, userName) { Instance = _instance, ServiceName = _serviceName },
                    ShaMutation.Remove(key)));
            }
        }
    }

    private void DiffStorageConfig(DiffInput input, ShaBreadcrumbMap map, List<DiffAction> actions)
    {
        if (input.StorageConfig is not { } config)
        {
            return; // non-EFP instance — never emits a storage event.
        }

        var key = ShaKey.StorageConfig();
        EmitUpsert(map, actions, key, config,
            c => new StorageConfigurationChanged(c) { Instance = _instance, ServiceName = _serviceName },
            c => new StorageConfigurationChanged(c) { Instance = _instance, ServiceName = _serviceName });
    }

    private static void EmitUpsert<TPayload>(
        ShaBreadcrumbMap map,
        List<DiffAction> actions,
        ShaKey key,
        TPayload payload,
        Func<TPayload, WaldurIntegrationEvent> created,
        Func<TPayload, WaldurIntegrationEvent> updated)
        where TPayload : notnull, IIdentityHashed
    {
        var hash = payload.ComputeIdentityHash();

        if (!map.TryGetHash(key, out var existing))
        {
            actions.Add(new DiffAction(created(payload), ShaMutation.Set(key, hash)));
        }
        else if (!string.Equals(existing, hash, StringComparison.Ordinal))
        {
            actions.Add(new DiffAction(updated(payload), ShaMutation.Set(key, hash)));
        }
    }

    private static List<AbsenceTransition> DetectAbsences(DiffInput input, ShaBreadcrumbMap map)
    {
        var presentProviders = new HashSet<string>(input.Providers.Select(p => p.ExternalId), StringComparer.Ordinal);
        var presentProjects = new HashSet<string>(input.Projects.Select(p => p.ExternalId), StringComparer.Ordinal);

        var absences = new List<AbsenceTransition>();
        foreach (var key in map.Keys)
        {
            // Membership absence is governed by the complete-snapshot rule (D7), not this sweep;
            // the storage-config slot is a singleton and never "absent". A per-entity-type list that
            // came back partial (ADR-0064) suppresses its own sweep: a breadcrumb missing from an
            // incomplete list may merely sit on the failed page, so it is not treated as absent.
            var absent = key.EntityType switch
            {
                WaldurEntityType.Provider => input.ProvidersComplete && !presentProviders.Contains(key.ExternalId),
                WaldurEntityType.Project => input.ProjectsComplete && !presentProjects.Contains(key.ExternalId),
                _ => false
            };

            if (absent)
            {
                absences.Add(new AbsenceTransition(key));
            }
        }

        return absences;
    }
}