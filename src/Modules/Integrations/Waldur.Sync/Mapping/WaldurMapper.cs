using System.Collections.Immutable;
using System.Globalization;

using UserOrg.Shared;

using Waldur.Contracts;
using Waldur.Contracts.Payloads;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;

namespace Waldur.Sync.Mapping;

/// <summary>
/// Pure API-models → <c>Waldur.Contracts</c> payloads mapper. No I/O, no DB, and deliberately
/// <b>no clock</b> — every value is a pure function of Waldur API data + instance configuration, so
/// payloads are reproducible from the DB later (ADR-0055/A1, round-trip guardian B11). Ineligible or
/// invalid items never throw and never produce a payload: they surface as <see cref="MappingSkip"/>.
///
/// Ports the transformation + validation behaviour of the legacy <c>WaldurEfpExporter</c> /
/// <c>PuhuriExporter</c> (identical mapping, config-parameterised), the three validation layers
/// (<c>AggregateDataValidator</c>, <c>EnsureValidator</c>, inline producer/consumer skip rules), and
/// the derivation helpers (SSH-URL grouping, longest-common-prefix naming, allocation-unit map,
/// deucalion trim).
/// </summary>
public sealed class WaldurMapper(
    WaldurInstanceOptions options,
    TimeProvider timeProvider)
{
    private readonly WaldurInstanceOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Maps a Waldur customer + its public offerings to a <see cref="ProviderPayload"/>. Locations are
    /// <b>derived</b> from the offerings (ADR-0054/A3): valid offerings (with an SSH-login endpoint)
    /// are grouped by that endpoint URL, named by the longest common prefix of the grouped offering
    /// names, and the endpoint has the <c>ssh://</c> scheme stripped for HPC.
    /// </summary>
    public ProviderMappingResult MapProvider(ApiCustomer customer, IReadOnlyList<ApiPublicOffering> offerings)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(offerings);

        var skips = new List<MappingSkip>();
        if (customer.Id == Guid.Empty)
        {
            skips.Add(new("Provider", ApplicationConstants.NotDefined, EMappingSkipReason.ProviderIdMissing, "Provider has an empty 'Id' attribute and cannot be exported.."));
            return new(null, skips);
        }

        var externalId = ToExternalId(customer.Id);
        var name = customer.Name?.RemoveControlCharacters()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            skips.Add(new("Provider", externalId, EMappingSkipReason.ProviderNameMissing, "Provider has an empty 'Name' attribute and cannot be exported.."));
            return new(null, skips);
        }

        if (string.IsNullOrWhiteSpace(customer.Country))
        {
            skips.Add(new("Provider", externalId, EMappingSkipReason.ProviderCountryMissing, "Provider has an empty 'Country' attribute and cannot be exported."));
            return new(null, skips);
        }

        var locations = DeriveLocations(externalId, offerings, skips);
        if (locations.Count == 0)
        {
            skips.Add(new("Provider", externalId, EMappingSkipReason.ProviderNoLocations, "Provider has no derivable locations and cannot be exported."));
            return new(null, skips);
        }

        var payload = new ProviderPayload(
            externalId,
            TruncateRequired(name, MappingConstants.NameMaxLength),
            CaseFold(customer.Country.Trim()),
            Truncate(Trimmed(customer.Description), MappingConstants.ExtendedDescriptionMaxLength),
            locations)
        {
            CreatedAt = customer.Created.UtcDateTime
        };

        return new(payload, skips);
    }

    /// <summary>
    /// Maps a Waldur project + its (offering-paired) marketplace resources to a
    /// <see cref="ProjectPayload"/> with embedded resources. Applies the eligibility rules
    /// (ADR-0054/A4): a resource is eligible iff it has allocations and an EffectiveId or BackendId;
    /// a project is eligible iff it has ≥1 eligible resource <b>or</b> is removed (A1). Ineligible →
    /// no payload. Resources are grouped by <c>(EffectiveId, BackendId, Offering.CustomerId)</c> with
    /// Min/Max date aggregation, then one payload per resource (assigned location).
    /// </summary>
    public ProjectMappingResult MapProject(ApiProject project, IImmutableList<ResourceOffering> resources)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(resources);

        var skips = new List<MappingSkip>();
        var externalId = ToExternalId(project.Id);

        if (project.Id == Guid.Empty)
        {
            skips.Add(new("Project", externalId, EMappingSkipReason.ProjectIdMissing, "Project has an empty 'Id' attribute and cannot be exported."));
            return new(null, skips);
        }

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            skips.Add(new("Project", externalId, EMappingSkipReason.ProjectNameMissing, "Project has an empty 'Name' attribute  and cannot be exported."));
            return new(null, skips);
        }

        var slug = project.Slug.Trim();
        if (!slug.IsValidShortName())
        {
            skips.Add(new("Project", externalId, EMappingSkipReason.ProjectSlugInvalidOrMissing, "Project has an empty/not valid 'Slug' attribute and cannot be exported."));
            return new(null, skips);
        }

        if (project.Created is null)
        {
            skips.Add(new("Project", externalId, EMappingSkipReason.ProjectCreatedMissing, "Project has an empty 'CreatedAt' attribute and cannot be exported."));
            return new(null, skips);
        }

        var eligible = FilterEligibleResources(resources, skips);
        var resourcePayloads = MapResources(eligible, skips);

        if (resourcePayloads.Count == 0 && !project.IsRemoved)
        {
            skips.Add(new("Project", externalId, EMappingSkipReason.ProjectNoEligibleResources, "Project has no eligible marketplace resources and cannot be exported."));
            return new(null, skips);
        }

        var createdAt = project.Created.Value.UtcDateTime;
        var payload = new ProjectPayload(
            externalId,
            TruncateRequired(project.Name.Trim(), MappingConstants.NameMaxLength),
            slug,
            Truncate(Trimmed(project.Description), MappingConstants.ExtendedDescriptionMaxLength),
            project.StartDate?.UtcDateTime ?? createdAt,
            project.EndDate?.UtcDateTime ?? DateTime.MaxValue,
            project.IsRemoved,
            resourcePayloads)
        {
            CreatedAt = createdAt
        };

        return new(payload, skips);
    }

    /// <summary>
    /// Maps a project's <c>list_users</c> snapshot to <see cref="ProjectUserPayload"/> members.
    /// Identity is <c>UserName</c> (ADR-0054/A2); the <b>mapped Userorg role</b> is persisted so the
    /// DB rebuilder reproduces it (unknown Waldur role → the same <c>Member</c> fallback the consumer
    /// uses, plus a structured warning so hashes stay stable). Email is create-only, not hashed.
    /// </summary>
    public MembershipMappingResult MapMembership(Guid projectId, IReadOnlyList<ApiProjectUser> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        var projectExternalId = ToExternalId(projectId);
        var members = new List<ProjectUserPayload>();
        var skips = new List<MappingSkip>();

        foreach (var user in users)
        {
            if (user.Id == Guid.Empty)
            {
                skips.Add(new("Member", projectExternalId, EMappingSkipReason.MemberUserIdentifierMissing, $"Member {ApplicationConstants.NotDefined} has an empty 'Id' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(user.UserName))
            {
                skips.Add(new("Member", projectExternalId, EMappingSkipReason.MemberUserNameMissing, $"Member {user.Id} has an empty 'Username' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(user.UserEmail))
            {
                skips.Add(new("Member", projectExternalId, EMappingSkipReason.MemberUserEmailMissing, $"Member {user.Id} has an empty 'UserEmail' attribute and cannot be exported."));
                continue;
            }

            var (role, unknown) = MapRole(user.RoleName);
            if (unknown)
            {
                skips.Add(new("Member", user.UserName, EMappingSkipReason.MemberUnknownRole, $"Unknown Waldur role '{user.RoleName}' for '{user.UserName}'; mapped to '{role}'."));
            }

            members.Add(new ProjectUserPayload(
                projectExternalId,
                user.UserName.Trim(),
                role,
                HasAccess: true)
            {
                Email = Trimmed(user.UserEmail),
                CreatedAt = user.Created.UtcDateTime
            });
        }

        return new(members, skips);
    }

    /// <summary>
    /// Maps the EFP storage-reconciliation configuration to a <see cref="StorageConfigPayload"/>. This
    /// is a pure function of config; the worker hashes it to drive <c>StorageConfigurationChanged</c>
    /// (ADR-0061). <see cref="StorageConfigPayload.EfpCoreServiceName"/> is the ServiceName the
    /// rebuilder excludes from all payload mapping (D14/ADR-0055/A4).
    /// </summary>
    public StorageConfigPayload MapStorageConfig()
    {
        var storage = _options.Storage
            ?? throw new InvalidOperationException("Storage config mapping requires Storage options (EFP instances only).");

        return new StorageConfigPayload(
            storage.EfpCoreServiceName,
            storage.UseSharedIrodsForAllEntities,
            storage.SharedIrods is null ? null : MapStorageInstance(null, storage.SharedIrods, "iRODS"),
            [.. storage.IrodsInstancesMap
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => MapStorageInstance(kv.Key, kv.Value, "iRODS"))],
            [.. storage.StagingAreaInstancesMap
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => MapStorageInstance(kv.Key, kv.Value, "Storage"))]);
    }

    /// <summary>
    /// Maps a raw Waldur role name to the persisted Userorg role name. Ported verbatim from
    /// <c>WaldurEfpExporter.CreateUserProjectAssignmentEntities</c>; unknown → <c>Member</c>.
    /// </summary>
    public static (string Role, bool Unknown) MapRole(string? waldurRole) =>
        waldurRole switch
        {
            MappingConstants.WaldurProjectManagerRoleName => (MappingConstants.UserorgManagerRoleName, false),
            MappingConstants.WaldurProjectMemberRoleName => (MappingConstants.UserorgMemberRoleName, false),
            MappingConstants.WaldurProjectAdminRoleName => (MappingConstants.UserorgOwnerRoleName, false),
            _ => (MappingConstants.UserorgMemberRoleName, true)
        };

    // ── Provider location derivation (ADR-0054/A3) ────────────────────────────

    private static ImmutableList<ProviderLocationPayload> DeriveLocations(
        string providerExternalId,
        IReadOnlyList<ApiPublicOffering> offerings,
        List<MappingSkip> skips)
    {
        var valid = new List<ApiPublicOffering>();
        foreach (var offering in offerings)
        {
            if (offering.Endpoints is null || !TryExtractSshLoginEndpoint(offering.Endpoints, out _))
            {
                skips.Add(new("Location", providerExternalId, EMappingSkipReason.LocationInvalidNoSshEndpoint, $"Offering '{offering.Name}' has no SSH-login endpoint and cannot be exported."));
                continue;
            }

            var offeringName = offering.Name?.RemoveControlCharacters();
            if (string.IsNullOrWhiteSpace(offeringName))
            {
                skips.Add(new("Location", providerExternalId, EMappingSkipReason.LocationNameMissing, $"Offering '{offering.Name}' has no 'Name' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(offering.Slug))
            {
                skips.Add(new("Location", providerExternalId, EMappingSkipReason.LocationSlugMissing, $"Offering '{offering.Name}' has no 'Slug' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(offering.State))
            {
                skips.Add(new("Location", providerExternalId, EMappingSkipReason.LocationStateMissing, $"Offering '{offering.Name}' has no 'State' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(offering.Type))
            {
                skips.Add(new("Location", providerExternalId, EMappingSkipReason.LocationTypeMissing, $"Offering '{offering.Name}' has no 'Type' attribute and cannot be exported."));
                continue;
            }

            valid.Add(offering);
        }

        var validLocationsByUrl = valid
            .SelectMany(l => l.Endpoints!
                .Where(e =>
                    string.Equals(e.Name, MappingConstants.LocationLoginNodeEndpointName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(e.Url))
                .Select(e => new { Location = l, Url = e.Url! }))
            .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                k => k.Key,
                v => v
                    .Select(s => s.Location)
                    .OrderBy(o => o.Id)
                    .ToList()
            );

        var locations = new List<ProviderLocationPayload>();
        foreach (var locationUrl in validLocationsByUrl)
        {
            var locationOfferings = locationUrl.Value;

            if (!locationOfferings.All(i => i.Type == locationOfferings[0].Type))
            {
                skips.Add(new("Location", providerExternalId, EMappingSkipReason.LocationDifferentType, $"Offering '{string.Join(",", locationOfferings.Select(s => s.Name))}' has different 'Type' attribute for same locations and cannot be exported."));
                continue;
            }

            var name = locationOfferings.Where(o => !string.IsNullOrEmpty(o.Name))
                .Select(o => o.Name.RemoveControlCharacters())
                .Aggregate(CommonPrefixIgnoreCase)
                    .TrimEnd('-')
                    .Trim()
                 ?? string.Empty;

            var description = Truncate(Trimmed(locationOfferings[0].Description), MappingConstants.DescriptionMaxLength);
            var locationType = locationOfferings[0].Type;
            var isShared = locationOfferings.All(a => a.IsShared)
                ? locationOfferings[0].IsShared
                : false;
            var isDeleted = locationOfferings.All(o => o.State.Equals(MappingConstants.WaldurStateArchived, StringComparison.OrdinalIgnoreCase));

            locations.Add(new ProviderLocationPayload(
                TruncateRequired(name, MappingConstants.NameMaxLength),
                NormalizeEndpoint(locationUrl.Key, locationType),
                CaseFold(locationType),
                description,
                isShared,
                isDeleted));
        }

        return locations.ToImmutableList();
    }

    private static List<ResourceOffering> FilterEligibleResources(
        IReadOnlyList<ResourceOffering> resources,
        List<MappingSkip> skips)
    {
        var eligible = new List<ResourceOffering>();
        foreach (var ro in resources)
        {
            if (ro.Resource.Id == Guid.Empty)
            {
                skips.Add(new("Resource", ApplicationConstants.NotDefined, EMappingSkipReason.ResourceIdentifierMissing, $"Resource has no 'Id' attribute and cannot be exported."));
                continue;
            }

            var resourceId = ToExternalId(ro.Resource.Id);
            var resourceEffectiveId = Trimmed(ro.Resource.EffectiveId?.RemoveControlCharacters());
            var resourceBackendId = Trimmed(ro.Resource.BackendId?.RemoveControlCharacters());
            var effectiveResourceId = resourceEffectiveId ?? resourceBackendId ?? ApplicationConstants.NotDefined;

            if (string.IsNullOrWhiteSpace(resourceEffectiveId) && string.IsNullOrWhiteSpace(resourceBackendId))
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceNoBackendOrEffectiveId, $"Resource '{effectiveResourceId}' has neither 'EffectiveId' or 'BackendId' attributes and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(ro.Resource.Name))
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceNameMissing, $"Resource '{effectiveResourceId}' has no 'Name' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(ro.Resource.State))
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceStateMissing, $"Resource '{effectiveResourceId}' has no 'State' attribute and cannot be exported."));
                continue;
            }

            if (ro.Resource.Allocations is null || ro.Resource.Allocations.Count == 0)
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceNoAllocations, $"Resource '{effectiveResourceId}' has no 'Allocations' attribute and cannot be exported."));
                continue;
            }

            if (ro.Offering is null)
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceOfferingMissing, $"Resource '{effectiveResourceId}' has no assigned offering and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(ro.Offering.Name))
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceOfferingNameMissing, $"Resource '{effectiveResourceId}' has no offering 'Name' attribute and cannot be exported."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(ro.Offering.State))
            {
                skips.Add(new("Resource", resourceId, EMappingSkipReason.ResourceOfferingStateMissing, $"Resource '{effectiveResourceId}' has no offering 'State' attribute and cannot be exported."));
                continue;
            }

            eligible.Add(ro);
        }

        return eligible;
    }

    private ImmutableList<ResourcePayload> MapResources(
        IReadOnlyList<ResourceOffering> eligible,
        List<MappingSkip> skips)
    {
        var payloads = new List<ResourcePayload>();

        var groupedResource = eligible.GroupBy(ro => new
        {
            ro.Resource.EffectiveId,
            ro.Resource.BackendId,
            CustomerId = ro.Offering!.CustomerId
        });

        foreach (var resGroup in groupedResource)
        {
            //TODO Log
            //logger.LogDebug($"Project {projectData.Project.Name} Grouped resources backendids {string.Join(",", groupedResources.SelectMany(s => s.Select(r => r.Resource.BackendId)))}");

            // Order by the resource UUID before the members[0] Description pick so the hashed scalar
            var members = resGroup
                .OrderBy(m => ToExternalId(m.Resource.Id), StringComparer.Ordinal)
                .ToList();

            var rawName = resGroup.Key.EffectiveId ?? resGroup.Key.BackendId!;
            var name = TruncateRequired(rawName.RemoveControlCharacters().Trim(), MappingConstants.NameMaxLength);

            var description = Truncate(Trimmed(members[0].Resource.Name), MappingConstants.DescriptionMaxLength);
            var startDate = members.Min(m => m.Resource.Created)?.UtcDateTime ?? timeProvider.GetUtcNow().UtcDateTime;
            var endDate = resGroup.Max(r => r.Resource.EndDate)?.UtcDateTime ?? DateTime.MaxValue;
            var isDeleted = members.All(m => m.Resource.State.Equals(MappingConstants.WaldurStateTerminated, StringComparison.OrdinalIgnoreCase));

            foreach (var member in members)
            {
                //TODO Log
                //logger.LogDebug($"Processing resource: ProjectExternalId={projectData.Project.Id}, EffectiveId={res.Resource.EffectiveId}, BackendId={res.Resource.BackendId}, OfferingId={res.Offering.Id}, CustomerId={res.Offering.CustomerId}");
                var offering = member.Offering!;
                if (offering.Endpoints is null || !TryExtractSshLoginEndpoint(offering.Endpoints, out var endpoint))
                {
                    skips.Add(new("Resource", ToExternalId(member.Resource.Id), EMappingSkipReason.ResourceNoSshEndpoint, $"Resource '{name}' has no SSH-login endpoint and cannot be exported."));
                    continue;
                }

                var normalizedEndpoint = NormalizeEndpoint(endpoint!, offering.Type);
                var (unitType, unitCount, values) = ExtractAllocation(member.Resource, offering, name);
                var (heappeUri, heappeIdentifier) = ResolveHeappe(offering.CustomerId);

                if (string.IsNullOrEmpty(heappeUri) || string.IsNullOrEmpty(heappeIdentifier))
                {
                    skips.Add(new("Resource", ToExternalId(member.Resource.Id), EMappingSkipReason.ResourceMappingMissing, $"Resource '{name}' has no HEAppE mapping and cannot be exported."));
                    continue;
                }

                //TODO Log
                //logger.LogDebug($"Location endpoint extracted: ProjectExternalId={projectData.Project.Id}, {locationEndpoint}");
                payloads.Add(new ResourcePayload(
                    name,
                    ToExternalId(member.Resource.Id),
                    description,
                    startDate,
                    endDate,
                    isDeleted,
                    normalizedEndpoint,
                    Trimmed(offering.Name) ?? string.Empty,
                    unitType ?? MappingConstants.UnspecifiedAllocationUnitTypeName,
                    unitCount,
                    heappeUri,
                    heappeIdentifier,
                    values)
                {
                    CreatedAt = member.Resource.Created?.UtcDateTime,
                    ModifiedAt = member.Resource.Modified?.UtcDateTime
                });
            }
        }

        return payloads.ToImmutableList();
    }

    private (string? UnitType, long? UnitCount, IImmutableList<ResourceAllocationValuePayload> Values) ExtractAllocation(
        ApiMarketplaceResource resource,
        ApiPublicOffering offering,
        string resourceName)
    {
        var allocations = resource.Allocations;
        var components = offering.Components;

        if (allocations is null || allocations.Count == 0 || components is null || components.Count == 0)
        {
            return (null, null, []);
        }

        var remaining = new List<KeyValuePair<string, decimal>>(allocations);
        string? unitType = null;
        long? unitCount = null;

        foreach (var mapping in _options.AllocationUnitMap)
        {
            if (string.IsNullOrWhiteSpace(mapping.WaldurKey))
            {
                continue;
            }

            var componentTypeName = components.FirstOrDefault(c => c.MeasuredUnit == mapping.WaldurKey)?.Type;
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                continue;
            }

            var entry = allocations.FirstOrDefault(kv => kv.Key == componentTypeName);
            unitCount = mapping.ConvertToUserorgUnit(entry.Value);
            unitType = mapping.UserorgKey;
            remaining.Remove(entry);
            break;
        }

        var values = remaining
            .Select(kv => new ResourceAllocationValuePayload(kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        // TEMP: deucalion cluster trims the last name char for the HEAppE base path only (R2 fixes this in Waldur).
        var nameForPath = resourceName;
        if (offering.Endpoints is not null
            && TryExtractSshLoginEndpoint(offering.Endpoints, out var loginEndpoint)
            && loginEndpoint!.Contains(MappingConstants.DeucalionLoginHost, StringComparison.OrdinalIgnoreCase)
            && nameForPath.Length > 0)
        {
            nameForPath = nameForPath[..^1];
        }

        var basePath = offering.PluginOptions?.HeappeLocalBasePath;
        if (!string.IsNullOrEmpty(basePath))
        {
            values.Add(new ResourceAllocationValuePayload(
                MappingConstants.HeappeLocalBasePathKey,
                $"{basePath.TrimEnd('/')}/{nameForPath}"));
        }

        return (unitType, unitCount, values.ToImmutableList());
    }

    private (string? Uri, string? Identifier) ResolveHeappe(Guid customerId)
    {
        var key = customerId.ToString("N", CultureInfo.InvariantCulture);
        if (!_options.HeappeInstancesMap.TryGetValue(key, out var heappe)
            || heappe.Uri is null
            || string.IsNullOrEmpty(heappe.Identifier))
        {
            return (null, null);
        }

        return (NormalizeHeappeUri(heappe.Uri), heappe.Identifier);
    }

    private static StorageInstancePayload MapStorageInstance(string? hostingKey, WaldurStorageInstanceOptions instance, string locationType) =>
        new(
            hostingKey is null ? null : NormalizeGuidKey(hostingKey),
            instance.Name,
            instance.Endpoint?.ToString() ?? string.Empty,
            locationType,
            instance.AllocationAmount);

    private static bool TryExtractSshLoginEndpoint(IReadOnlyList<ApiPublicOfferingEndpoint> endpoints, out string? url)
    {
        var endpoint = endpoints.FirstOrDefault(e =>
            string.Equals(e.Name, MappingConstants.LocationLoginNodeEndpointName, StringComparison.OrdinalIgnoreCase));

        if (endpoint is not null && !string.IsNullOrWhiteSpace(endpoint.Url))
        {
            url = endpoint.Url;
            return true;
        }

        url = null;
        return false;
    }

    private static string CommonPrefixIgnoreCase(string a, string b)
    {
        var len = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < len && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
        {
            i++;
        }

        return a[..i].Trim();
    }

    private static string NormalizeEndpoint(string url, string? locationType) =>
        string.Equals(locationType, MappingConstants.WaldurResourceTypeHpc, StringComparison.Ordinal)
            ? url.Replace(MappingConstants.WaldurSshUriScheme, string.Empty, StringComparison.OrdinalIgnoreCase).Trim()
            : url.Trim();

    private static string NormalizeHeappeUri(Uri uri)
    {
        var absolute = uri.AbsoluteUri;
        return absolute.EndsWith('/') ? absolute : $"{absolute}/";
    }

    private static string ToExternalId(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    private static string NormalizeGuidKey(string key) => Guid.TryParse(key, out var guid)
        ? ToExternalId(guid)
        : key;

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value)
        ? default
        : value.Trim();

    /// <summary>
    /// Case-fold a reference-name field the consumer resolves case-INsensitively (Country,
    /// LocationType — SQL Server default collation). Hashing the folded value keeps the SHA stable
    /// across a restart: the DB stores the canonical-cased reference row, so the cold rebuild reads
    /// that casing back — hashing the raw Waldur casing here would diverge and storm ProviderUpdated
    /// every restart (ADR-0055/A2 amendment, review W1). The rebuilder folds the same fields.
    /// </summary>
#pragma warning disable CA1308 // Deliberate lowercase fold for a byte-stable, case-insensitive hash — not security normalization.
    private static string CaseFold(string value) => value.ToLowerInvariant();
#pragma warning restore CA1308

    /// <summary>Truncate a required (non-null) string to a DB limit (A2.2).</summary>
    private static string TruncateRequired(string value, int max) => value.Length > max
        ? value[..max]
        : value;

    /// <summary>Truncate an optional string to a DB limit; null/empty stays null.</summary>
    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max
        ? value[..max]
        : value;
}