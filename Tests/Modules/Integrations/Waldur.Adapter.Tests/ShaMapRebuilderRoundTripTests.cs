using Forge.Core.Features.Role.Services;

using System.Collections.Immutable;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Sync;
using Userorg.Adapter.Waldur.Sync.Handlers;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;
using Waldur.Sync.Diff;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Phase-4.8 round-trip hash guardian (D1/D13, ADR-0055): for every adversarial vector we build an
/// API-shaped <c>Waldur.Contracts</c> payload P, apply it with the REAL consumer handlers to a
/// <see cref="FakeUserOrgDbContext"/>, rebuild P' from the DB with
/// <see cref="WaldurShaMapRebuilder"/>, and assert <c>Hash(P) == Hash(P')</c> per <see cref="ShaKey"/>.
/// A vector that fails to round-trip is a real DB↔API mapping drift that would storm events every
/// restart. The five CRITICAL invariants are each pinned: EfpCore exclusion, mapped-role read-back,
/// verbatim HeappeUri, config-slot never rebuilt, soft-deleted projects included.
/// </summary>
public sealed class ShaMapRebuilderRoundTripTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private const string DomainName = "EFP-Service";
    private const string EfpCore = "EFP-Core";
    private const string HpcEndpoint = "karolina.it4i.cz";

    private static readonly Guid ProviderExternalId = Guid.NewGuid();
    private static readonly string ProjectExternalId = Guid.NewGuid().ToString();

    private const int OwnerRoleId = 10;
    private const int ManagerRoleId = 11;
    private const int MemberRoleId = 12;
    private const int PlatformUserRoleId = 1;

    private static IList<RoleInfo> Roles() =>
    [
        Role(OwnerRoleId, RoleInfoConstants.ProjectOwnerRoleName),
        Role(ManagerRoleId, RoleInfoConstants.ProjectManagerRoleName),
        Role(MemberRoleId, RoleInfoConstants.ProjectMemberRoleName),
        Role(PlatformUserRoleId, RoleInfoConstants.PlatformUserRoleName),
    ];

    private static RoleInfo Role(int id, string name) => new(id, name, name, "Project", [], true, id);

    // ── Payload builders (already-normalized, as the B03 mapper would emit) ────

    // Country/LocationType are case-FOLDED (lowercase) — that is what the B03 mapper now emits (W1),
    // because the consumer resolves those reference names case-insensitively.
    private static ProviderPayload ProviderPayload(IImmutableList<ProviderLocationPayload>? locations = null) =>
        new(
            ProviderExternalId.ToString("D"),
            "Karolina Provider",
            "cz",
            "provider description",
            locations ?? [HpcLocation()]);

    private static ProviderLocationPayload HpcLocation() =>
        new("karolina", HpcEndpoint, "hpc", "hpc location", IsShared: true, IsDeleted: false);

    private static ProjectPayload ProjectPayload(
        string name = "Acme Project",
        bool isRemoved = false,
        DateTime? startDate = null,
        DateTime? endDate = null,
        IImmutableList<ResourcePayload>? resources = null) =>
        new(
            ProjectExternalId,
            name,
            "acme-proj",
            "project description",
            startDate ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            isRemoved,
            resources ?? [HpcResource()]);

    private static ResourcePayload HpcResource(
        string name = "res-1",
        string? resourceExternalId = null,
        string heappeUri = "HTTPS://HeaPPe.TEST/verbatim/",
        string heappeIdentifier = "heappe-id-1",
        DateTime? startDate = null,
        DateTime? endDate = null) =>
        new(
            name,
            resourceExternalId ?? Guid.NewGuid().ToString("D"),
            "resource description",
            startDate ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endDate ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsDeleted: false,
            HpcEndpoint,
            "Karolina Offering",
            "core-hours",
            100,
            heappeUri,
            heappeIdentifier,
            [new ResourceAllocationValuePayload("HEAPPE_LOCAL_BASE_PATH", $"/data/{name}")]);

    private static ProjectUserPayload MemberPayload(string userName, string role) =>
        new(ProjectExternalId, userName, role, HasAccess: true) { Email = $"{userName}@test", CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

    private static StorageConfigPayload StorageConfig() =>
        new(
            EfpCore,
            UseSharedIrodsForAllEntities: false,
            SharedIrods: null,
            IrodsInstances: [new StorageInstancePayload(ProviderExternalId.ToString("N"), "prov-irods", "irods://prov", "iRODS", 100)],
            StagingAreaInstances: [new StorageInstancePayload(ProviderExternalId.ToString("N"), "prov-staging", "file://staging", "Storage", null)]);

    private static FakeUserOrgDbContext SeededContext()
    {
        var context = new FakeUserOrgDbContext();
        // Seed the reference rows with the SAME (folded) casing the payload carries — the InMemory
        // provider matches ordinally, so the handler path needs an exact-case row to resolve. The
        // rebuilder's case-fold is exercised separately by Should_CaseFold_ReferenceFields_On_Rebuild.
        context.Seed<Country>([new Country { Id = 1, Name = "cz" }]);
        context.Seed<LocationType>(
        [
            new LocationType { Id = (int)ELocationType.HPC, Name = "hpc" },
            new LocationType { Id = (int)ELocationType.Storage, Name = "Storage" },
            new LocationType { Id = (int)ELocationType.iRODS, Name = "iRODS" },
        ]);
        context.Seed<AllocationUnitType>(
        [
            new AllocationUnitType { Id = 1, Name = "unspecified" },
            new AllocationUnitType { Id = 2, Name = "core-hours" },
            new AllocationUnitType { Id = 4, Name = "GB" },
        ]);
        context.Seed<User>([new User
        {
            Id = Guid.CreateVersion7(),
            IdentityProviderUserId = ServiceName,
            Email = "svc@waldur",
            UserName = ServiceName,
            CreatedAt = DateTime.UtcNow,
        }]);
        return context;
    }

    private static IWaldurConsumerOptions Options() =>
        new WaldurConsumerOptions([new WaldurConsumerInstanceOptions(ServiceName, DomainName, "SshCertificate", "pi@efp.test")]);

    private static async Task ApplyProviderAsync(IUserOrgDbContext context, ProviderPayload payload)
    {
        var handler = new ProviderUpsertHandler(context, NullLogger<ProviderUpsertHandler>.Instance);
        await handler.HandleAsync(new ProviderCreated(payload) { Instance = Instance, ServiceName = ServiceName }, CancellationToken.None);
    }

    private static async Task ApplyProjectAsync(IUserOrgDbContext context, ProjectPayload payload, bool updated = false)
    {
        var handler = new ProjectUpsertHandler(
            context,
            Options(),
            new FakeProjectCreationService(),
            new FakeHeappeService(),
            new FakeHeappeConnectionFactory(),
            new FakeHeappeSynchronizationService(),
            NullLogger<ProjectUpsertHandler>.Instance);

        if (updated)
        {
            await handler.HandleAsync(new ProjectUpdated(payload) { Instance = Instance, ServiceName = ServiceName }, CancellationToken.None);
        }
        else
        {
            await handler.HandleAsync(new ProjectCreated(payload) { Instance = Instance, ServiceName = ServiceName }, CancellationToken.None);
        }
    }

    private static async Task ApplyMemberAsync(IUserOrgDbContext context, ProjectUserPayload payload, IList<RoleInfo> roles)
    {
        var handler = new UserAssignedHandler(context, new FakeRoleListService(roles), NullLogger<UserAssignedHandler>.Instance);
        await handler.HandleAsync(new UserAssigned(payload) { Instance = Instance, ServiceName = ServiceName }, CancellationToken.None);
    }

    private static async Task ApplyStorageAsync(IUserOrgDbContext context, StorageConfigPayload config)
    {
        var handler = new StorageReconciliationHandler(context, NullLogger<StorageReconciliationHandler>.Instance);
        await handler.HandleAsync(new StorageConfigurationChanged(config) { Instance = Instance, ServiceName = ServiceName }, CancellationToken.None);
    }

    private static Task<IReadOnlyDictionary<ShaKey, string>> RebuildAsync(IUserOrgDbContext context, IList<RoleInfo>? roles = null) =>
        WaldurShaMapRebuilder.RebuildCoreAsync(context, new FakeRoleListService(roles ?? Roles()), ServiceName, CancellationToken.None);

    [Fact(DisplayName = "Baseline: provider + project + resources + members all round-trip")]
    public async Task Should_RoundTrip_Baseline()
    {
        using var context = SeededContext();
        var provider = ProviderPayload();
        var resource = HpcResource();
        var project = ProjectPayload(resources: [resource]);
        var owner = MemberPayload("alice", RoleInfoConstants.ProjectOwnerRoleName);
        var manager = MemberPayload("bob", RoleInfoConstants.ProjectManagerRoleName);
        var member = MemberPayload("carol", RoleInfoConstants.ProjectMemberRoleName);

        await ApplyProviderAsync(context, provider);
        await ApplyProjectAsync(context, project);
        await ApplyMemberAsync(context, owner, Roles());
        await ApplyMemberAsync(context, manager, Roles());
        await ApplyMemberAsync(context, member, Roles());

        var map = await RebuildAsync(context);

        Assert.Equal(provider.ComputeIdentityHash(), map[ShaKey.Provider(provider.ExternalId)]);
        Assert.Equal(project.ComputeIdentityHash(), map[ShaKey.Project(project.ExternalId)]);
        Assert.Equal(owner.ComputeIdentityHash(), map[ShaKey.Member(ProjectExternalId, "alice")]);
        Assert.Equal(manager.ComputeIdentityHash(), map[ShaKey.Member(ProjectExternalId, "bob")]);
        Assert.Equal(member.ComputeIdentityHash(), map[ShaKey.Member(ProjectExternalId, "carol")]);
    }

    [Fact(DisplayName = "Over-length (already-truncated) strings round-trip without a second truncation")]
    public async Task Should_RoundTrip_OverLengthStrings()
    {
        using var context = SeededContext();
        var longName = new string('x', 500);
        var project = ProjectPayload(name: longName, resources: []);

        await ApplyProjectAsync(context, project);
        var map = await RebuildAsync(context);

        Assert.Equal(project.ComputeIdentityHash(), map[ShaKey.Project(project.ExternalId)]);
    }

    [Fact(DisplayName = "D3: a row already stored with a dirty name converges after the first Updated pass")]
    public async Task Should_RoundTrip_DirtyNameConvergesAfterFirstUpdatedPass()
    {
        // D3/ADR-0055: WaldurShaMapRebuilder reads Project.Name back verbatim ("Stored shapes read
        // as-is", class remarks §5), so a row stored dirty stays dirty in the rebuilt map and
        // converges only once Project.SyncFrom writes the clean Name. Both halves are asserted: a
        // rebuilder that re-normalized would fail the first, a SyncFrom that dropped Name the second.
        // EF InMemory enforces neither nvarchar(50) truncation nor the unique index, so neither is
        // provable here; the 50-char rule is enforced and tested in the mapper (B02).
        using var context = SeededContext();
        var dirtyProject = ProjectPayload(name: "Acme \u0007Project\u0000", resources: []);
        await ApplyProjectAsync(context, dirtyProject);

        var cleanProject = ProjectPayload(name: "Acme Project", resources: []);
        var beforeUpdate = await RebuildAsync(context);

        Assert.Equal(dirtyProject.ComputeIdentityHash(), beforeUpdate[ShaKey.Project(dirtyProject.ExternalId)]);
        Assert.NotEqual(cleanProject.ComputeIdentityHash(), beforeUpdate[ShaKey.Project(cleanProject.ExternalId)]);

        await ApplyProjectAsync(context, cleanProject, updated: true);

        var map = await RebuildAsync(context);

        Assert.Equal(cleanProject.ComputeIdentityHash(), map[ShaKey.Project(cleanProject.ExternalId)]);
    }

    [Fact(DisplayName = "Null-date fallbacks (StartDate MinValue, EndDate MaxValue) round-trip")]
    public async Task Should_RoundTrip_NullDateFallbacks()
    {
        using var context = SeededContext();
        var resource = HpcResource(startDate: DateTime.MinValue, endDate: DateTime.MaxValue);
        var project = ProjectPayload(startDate: DateTime.MinValue, endDate: DateTime.MaxValue, resources: [resource]);

        await ApplyProviderAsync(context, ProviderPayload());
        await ApplyProjectAsync(context, project);
        var map = await RebuildAsync(context);

        Assert.Equal(project.ComputeIdentityHash(), map[ShaKey.Project(project.ExternalId)]);
    }

    [Fact(DisplayName = "Verbatim HeappeUri (un-normalized casing) round-trips exactly")]
    public async Task Should_RoundTrip_VerbatimHeappeUri()
    {
        using var context = SeededContext();
        var resource = HpcResource(heappeUri: "HTTPS://Weird.CASE.example/Path/");
        var project = ProjectPayload(resources: [resource]);

        await ApplyProviderAsync(context, ProviderPayload());
        await ApplyProjectAsync(context, project);
        var map = await RebuildAsync(context);

        Assert.Equal(project.ComputeIdentityHash(), map[ShaKey.Project(project.ExternalId)]);
    }

    [Fact(DisplayName = "D11: a resource name carrying control characters and outer whitespace round-trips verbatim (it is the upsert key)")]
    public async Task Should_RoundTrip_DirtyResourceName()
    {
        // D11/ADR-0055: ProjectResource.Name is the consumer's upsert key
        // (ProjectUpsertHandler.MapResource, WaldurShaMapRebuilder), so it is deliberately NOT
        // control-stripped or trimmed anywhere on either side. The leading/trailing whitespace is
        // not test noise: WaldurMapper.TruncateRequired (WaldurMapper.cs:578-579) truncates a
        // resource name to the DB length limit without a follow-up trim, so a mapped name can
        // legitimately end in whitespace. If a future edit adds stripping or a plain .Trim() to
        // either the consumer write or the rebuilder read, this test goes red.
        using var context = SeededContext();
        var dirtyResource = HpcResource(name: " Res \u0007Name\u0000 ");
        var project = ProjectPayload(resources: [dirtyResource]);

        await ApplyProviderAsync(context, ProviderPayload());
        await ApplyProjectAsync(context, project);
        var map = await RebuildAsync(context);

        Assert.Equal(project.ComputeIdentityHash(), map[ShaKey.Project(project.ExternalId)]);
    }

    [Fact(DisplayName = "Storage reconciliation (EfpCore rows) leaves the project + provider hash UNCHANGED (exclusion)")]
    public async Task Should_NotChangeHash_When_StorageReconciled()
    {
        using var context = SeededContext();
        var provider = ProviderPayload();
        var project = ProjectPayload(resources: [HpcResource()]);

        await ApplyProviderAsync(context, provider);
        await ApplyProjectAsync(context, project);
        var before = await RebuildAsync(context);

        await ApplyStorageAsync(context, StorageConfig());
        var after = await RebuildAsync(context);

        Assert.Equal(project.ComputeIdentityHash(), after[ShaKey.Project(project.ExternalId)]);
        Assert.Equal(provider.ComputeIdentityHash(), after[ShaKey.Provider(provider.ExternalId)]);
        Assert.Equal(before[ShaKey.Project(project.ExternalId)], after[ShaKey.Project(project.ExternalId)]);
        Assert.Equal(before[ShaKey.Provider(provider.ExternalId)], after[ShaKey.Provider(provider.ExternalId)]);
    }

    [Fact(DisplayName = "W1: case-insensitive reference fields (Country/LocationType) are case-folded on rebuild → no restart storm")]
    public async Task Should_CaseFold_ReferenceFields_On_Rebuild()
    {
        // Simulate the production reality the ordinal InMemory provider hides: the consumer resolved
        // Country/LocationType case-INsensitively under SQL Server, so the DB rows carry the CANONICAL
        // casing ("Czechia"/"HPC") while the mapper hashed the case-folded value. Seed those rows
        // directly (bypassing the handler, which under InMemory would not match cross-case), rebuild,
        // and assert the hash equals the mapper's folded payload — i.e. no spurious ProviderUpdated.
        using var context = new FakeUserOrgDbContext();
        var country = new Country { Id = 1, Name = "Czechia" };
        var hpcType = new LocationType { Id = (int)ELocationType.HPC, Name = "HPC" };
        context.Seed<Country>([country]);
        context.Seed<LocationType>([hpcType]);

        var provider = new Provider
        {
            Id = Guid.CreateVersion7(),
            Name = "Karolina Provider",
            Description = "provider description",
            Country = country,
            ServiceName = ServiceName,
            ExternalId = ProviderExternalId.ToString("D"),
            CreatedAt = DateTime.UtcNow,
        };
        context.Add(provider);
        context.Add(new Location
        {
            Id = Guid.CreateVersion7(),
            Name = "karolina",
            Description = "hpc location",
            Endpoint = HpcEndpoint,
            IsShared = true,
            IsDeleted = false,
            ProviderId = provider.Id,
            LocationTypeId = hpcType.Id,
            ServiceName = ServiceName,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var map = await RebuildAsync(context);

        // The mapper emits the folded casing ("czechia"/"hpc") from raw Waldur data.
        var expected = new ProviderPayload(
            ProviderExternalId.ToString("D"),
            "Karolina Provider",
            "czechia",
            "provider description",
            [new ProviderLocationPayload("karolina", HpcEndpoint, "hpc", "hpc location", IsShared: true, IsDeleted: false)]);

        Assert.Equal(expected.ComputeIdentityHash(), map[ShaKey.Provider(provider.ExternalId!)]);
    }

    [Fact(DisplayName = "Config slot is never rebuilt (ADR-0055/A4)")]
    public async Task Should_NotEmitConfigSlot()
    {
        using var context = SeededContext();
        await ApplyProjectAsync(context, ProjectPayload(resources: []));
        await ApplyStorageAsync(context, StorageConfig());

        var map = await RebuildAsync(context);

        Assert.DoesNotContain(ShaKey.StorageConfig(), map.Keys);
        Assert.DoesNotContain(map.Keys, k => k.EntityType == WaldurEntityType.StorageConfig);
    }

    [Fact(DisplayName = "Soft-deleted (removed) project is INCLUDED in the map and its hash is stable")]
    public async Task Should_IncludeRemovedProject_And_RoundTrip()
    {
        using var context = SeededContext();
        await ApplyProviderAsync(context, ProviderPayload());
        var resource = HpcResource();
        await ApplyProjectAsync(context, ProjectPayload(resources: [resource]));

        // Now the project is removed — resources stay in the DB (handler does not tear them down).
        var removed = ProjectPayload(isRemoved: true, resources: [resource]);
        await ApplyProjectAsync(context, removed, updated: true);

        var map = await RebuildAsync(context);

        Assert.Contains(ShaKey.Project(ProjectExternalId), map.Keys);
        Assert.Equal(removed.ComputeIdentityHash(), map[ShaKey.Project(ProjectExternalId)]);
    }

    [Fact(DisplayName = "EfpCore-owned provider/location rows are excluded from the rebuilt map")]
    public async Task Should_ExcludeEfpCoreProvider_FromMap()
    {
        using var context = SeededContext();
        await ApplyProviderAsync(context, ProviderPayload());
        await ApplyProjectAsync(context, ProjectPayload(resources: [HpcResource()]));
        await ApplyStorageAsync(context, StorageConfig());

        var map = await RebuildAsync(context);

        // The synthetic EFP-Core provider has ServiceName "EFP-Core" and a null ExternalId — it must
        // never appear as a Provider key, and only the one real provider is present.
        Assert.Single(map.Keys, k => k.EntityType == WaldurEntityType.Provider);
        Assert.Contains(ShaKey.Provider(ProviderExternalId.ToString("D")), map.Keys);
    }

    [Fact(DisplayName = "Rebuilder is resolvable through the consumer composition seam")]
    public void Should_RegisterRebuilder_ViaComposition()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddSingleton<IRoleListService>(new FakeRoleListService(Roles()));
        services.AddWaldurShaMapRebuilder(Instance, ServiceName);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IShaMapRebuilder>());
    }
}
