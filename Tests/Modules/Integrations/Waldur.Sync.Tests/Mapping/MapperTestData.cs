using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;
using Waldur.Sync.Mapping;

namespace Waldur.Sync.Tests.Mapping;

/// <summary>Builders with sane defaults for the Waldur API models, so tests state only what matters.</summary>
internal static class MapperTestData
{
    public static readonly Guid CustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid OfferingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ResourceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly DateTimeOffset Created = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>
    /// Default HEAppE mapping keyed by <see cref="CustomerId"/>, so builders/tests that don't care
    /// about HEAppE resolution get a resource that is still eligible (ResolveHeappe requires a
    /// mapping entry; see WaldurMapper.MapResources). Tests exercising the "no mapping" path should
    /// pass an explicit empty dictionary instead.
    /// </summary>
    public static Dictionary<string, WaldurHeappeInstanceOptions> DefaultHeappeMap() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [CustomerId.ToString("N")] = new() { Uri = new Uri("https://heappe.example"), Identifier = "HP-default" }
        };

    public static WaldurInstanceOptions Options(
        string serviceName = "EFP-Waldur",
        IDictionary<string, WaldurHeappeInstanceOptions>? heappe = null,
        WaldurStorageOptions? storage = null) =>
        new()
        {
            Name = "EFP",
            Enabled = true,
            Identity = new WaldurInstanceIdentityOptions { ServiceName = serviceName },
            HeappeInstancesMap = heappe ?? DefaultHeappeMap(),
            Storage = storage
        };

    public static WaldurMapper Mapper(
        string serviceName = "EFP-Waldur",
        IDictionary<string, WaldurHeappeInstanceOptions>? heappe = null,
        WaldurStorageOptions? storage = null) =>
        new(Options(serviceName, heappe, storage), TimeProvider.System);

    public static ApiCustomer Customer(
        Guid? id = null,
        string name = "Provider Inc",
        string? country = "Czechia",
        string? description = "A provider") =>
        new(id ?? CustomerId, name, "Display", description, country, Created);

    public static ApiPublicOfferingEndpoint SshEndpoint(string url = "ssh://hpc.example.org") =>
        new(Guid.NewGuid(), "SSH login", url);

    public static ApiPublicOfferingComponent Component(string type = "cpu", string measuredUnit = "core-hours") =>
        new(Guid.NewGuid(), "CPU", type, measuredUnit, null);

    public static ApiPublicOffering Offering(
        Guid? id = null,
        string name = "HPC Cluster A",
        string type = "HPC",
        string state = "active",
        bool shared = false,
        Guid? customerId = null,
        IReadOnlyList<ApiPublicOfferingEndpoint>? endpoints = null,
        IReadOnlyList<ApiPublicOfferingComponent>? components = null,
        string? heappeBasePath = null) =>
        new(
            id ?? OfferingId,
            name,
            "desc",
            endpoints ?? [SshEndpoint()],
            components ?? [Component()],
            customerId ?? CustomerId,
            Created,
            state,
            "slug",
            type,
            shared,
            heappeBasePath is null ? null : new ApiPluginOptions(heappeBasePath));

    public static ApiProject Project(
        Guid? id = null,
        string name = "My Project",
        string slug = "my-project",
        DateTimeOffset? created = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        bool isRemoved = false) =>
        new(id ?? ProjectId, name, slug, "desc", startDate, endDate, created ?? Created, isRemoved);

    public static ApiMarketplaceResource Resource(
        Guid? id = null,
        string name = "res-a",
        string? effectiveId = "eff-a",
        string? backendId = "back-a",
        string state = "active",
        Guid? offeringId = null,
        IReadOnlyDictionary<string, decimal>? allocations = null,
        DateTimeOffset? created = null,
        DateTimeOffset? endDate = null,
        DateTimeOffset? modified = null) =>
        new(
            id ?? ResourceId,
            name,
            "desc",
            ProjectId,
            offeringId ?? OfferingId,
            "HPC Cluster A",
            allocations ?? new Dictionary<string, decimal> { ["cpu"] = 5m },
            state,
            endDate,
            created ?? Created,
            modified)
        {
            EffectiveId = effectiveId,
            BackendId = backendId
        };

    public static ApiProjectUser User(
        string userName = "alice",
        string roleName = "PROJECT.MEMBER",
        string? email = "alice@example.org",
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), roleName, Guid.NewGuid(), userName, email, Created, null);
}
