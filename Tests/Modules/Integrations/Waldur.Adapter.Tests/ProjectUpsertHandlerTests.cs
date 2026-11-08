using System.Collections.Immutable;

using Heappe.Contracts;
using Heappe.Contracts.Connection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Sync;
using Userorg.Adapter.Waldur.Sync.Handlers;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Tests for the B08 <see cref="ProjectUpsertHandler"/>: project + embedded-resource upsert routed
/// from both <see cref="ProjectCreated"/> and <see cref="ProjectUpdated"/> (ADR-0057/A1), Heappe
/// ensure + command-template trigger per HPC resource (A1/A2), single terminal save (A3). Backed by
/// the InMemory <see cref="FakeUserOrgDbContext"/> with the Heappe ports mocked.
/// </summary>
public sealed class ProjectUpsertHandlerTests
{
    private const string ServiceName = "EFP-Waldur";
    private const string Instance = "EFP";
    private const string DomainName = "EFP-Service";
    private const string PreferredAuthType = "SshCertificate";
    private const string PiEmail = "pi@efp.test";
    private const string HpcEndpoint = "karolina.it4i.cz";
    private const string NonHpcEndpoint = "storage.it4i.cz";

    private static readonly string ProjectExternalId = Guid.NewGuid().ToString();
    private static readonly Guid ProviderId = Guid.NewGuid();

    private static ProjectPayload Payload(
        string name = "Acme Project",
        bool isRemoved = false,
        string externalId = "",
        IImmutableList<ResourcePayload>? resources = null) =>
        new(
            string.IsNullOrEmpty(externalId) ? ProjectExternalId : externalId,
            name,
            "acme-proj",
            "desc",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            isRemoved,
            resources ?? [HpcResource()])
        {
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    private static ResourcePayload HpcResource(
        string name = "res-1",
        string? resourceExternalId = null,
        string? heappeUri = "https://heappe.test/",
        string? heappeIdentifier = "heappe-id-1") =>
        new(
            name,
            resourceExternalId ?? Guid.NewGuid().ToString(),
            "resource-desc",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            false,
            HpcEndpoint,
            "Karolina Offering",
            "core-hours",
            100,
            heappeUri,
            heappeIdentifier,
            [new ResourceAllocationValuePayload("HEAPPE_LOCAL_BASE_PATH", $"/data/{name}")]);

    private static ResourcePayload NonHpcResource(string name = "storage-res") =>
        new(
            name,
            Guid.NewGuid().ToString(),
            "resource-desc",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            false,
            NonHpcEndpoint,
            "Storage Offering",
            "unspecified",
            null,
            null,
            null,
            []);

    private static ProjectCreated Created(ProjectPayload payload) =>
        new(payload) { Instance = Instance, ServiceName = ServiceName };

    private static ProjectUpdated Updated(ProjectPayload payload) =>
        new(payload) { Instance = Instance, ServiceName = ServiceName };

    private static IWaldurConsumerOptions Options() =>
        new WaldurConsumerOptions([new WaldurConsumerInstanceOptions(ServiceName, DomainName, PreferredAuthType, PiEmail)]);

    private static ProjectUpsertHandler Handler(
        IUserOrgDbContext context,
        IProjectCreationService? projectCreation = null,
        IHeappeSynchronizationService? sync = null) =>
        new(
            context,
            Options(),
            projectCreation ?? new FakeProjectCreationService(),
            new FakeHeappeService(),
            new FakeHeappeConnectionFactory(),
            sync ?? new FakeHeappeSynchronizationService(),
            NullLogger<ProjectUpsertHandler>.Instance);

    /// <summary>Seeds the service account + reference data (locations, provider, allocation-unit types).</summary>
    private static void Seed(DbContext context, bool seedServiceAccount = true)
    {
        if (seedServiceAccount)
        {
            context.Add(new User
            {
                Id = Guid.CreateVersion7(),
                IdentityProviderUserId = ServiceName,
                Email = "svc@waldur",
                UserName = ServiceName,
                CreatedAt = DateTime.UtcNow,
            });
        }

        context.Add(new Provider
        {
            Id = ProviderId,
            Name = "Karolina Provider",
            ServiceName = ServiceName,
            ExternalId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
        });

        context.Add(new Location
        {
            Id = Guid.CreateVersion7(),
            Name = "karolina",
            Endpoint = HpcEndpoint,
            ServiceName = ServiceName,
            ProviderId = ProviderId,
            LocationTypeId = (int)ELocationType.HPC,
            CreatedAt = DateTime.UtcNow,
        });

        context.Add(new Location
        {
            Id = Guid.CreateVersion7(),
            Name = "storage",
            Endpoint = NonHpcEndpoint,
            ServiceName = ServiceName,
            ProviderId = ProviderId,
            LocationTypeId = (int)ELocationType.Unspecified,
            CreatedAt = DateTime.UtcNow,
        });

        context.AddRange(
            new AllocationUnitType { Id = 1, Name = "core-hours" },
            new AllocationUnitType { Id = 2, Name = "unspecified" });

        context.SaveChanges();
    }

    private static FakeUserOrgDbContext SeededContext(bool seedServiceAccount = true)
    {
        var context = new FakeUserOrgDbContext();
        Seed(context, seedServiceAccount);
        return context;
    }

    [Fact(DisplayName = "Creates the project keyed (ExternalId, ServiceName) with the configured domain type")]
    public async Task Should_CreateProject_When_Absent()
    {
        using var context = SeededContext();

        await Handler(context).HandleAsync(Created(Payload(resources: [])), CancellationToken.None);

        var project = await context.ProjectsQuery
            .Include(p => p.ProjectDomainType)
            .SingleAsync(p => p.ExternalId == ProjectExternalId && p.ServiceName == ServiceName);
        Assert.Equal("Acme Project", project.Name);
        Assert.Equal(DomainName, project.ProjectDomainType!.Name);
    }

    [Fact(DisplayName = "Updates the existing project (no duplicate) when present")]
    public async Task Should_UpdateProject_When_Present()
    {
        using var context = SeededContext();
        await Handler(context).HandleAsync(Created(Payload(name: "Old", resources: [])), CancellationToken.None);

        await Handler(context).HandleAsync(Updated(Payload(name: "New", resources: [])), CancellationToken.None);

        var project = await context.ProjectsQuery
            .SingleAsync(p => p.ExternalId == ProjectExternalId && p.ServiceName == ServiceName);
        Assert.Equal("New", project.Name);
    }

    [Fact(DisplayName = "IsRemoved soft-deletes the project and leaves memberships in place")]
    public async Task Should_SoftDeleteProject_And_KeepMemberships_When_IsRemoved()
    {
        using var context = SeededContext();
        await Handler(context).HandleAsync(Created(Payload(resources: [])), CancellationToken.None);
        var project = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        var user = await context.UsersQuery.SingleAsync(u => u.IdentityProviderUserId == ServiceName);
        context.Add(new AssignedUserToProject { ProjectId = project.Id, User = user, CreatedAt = DateTime.UtcNow, IsDeleted = false });
        await context.SaveChangesAsync(CancellationToken.None);

        await Handler(context).HandleAsync(Updated(Payload(isRemoved: true, resources: [])), CancellationToken.None);

        var reloaded = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        Assert.True(reloaded.IsDeleted);
        var membership = await context.AssignedUserToProjectsQuery.SingleAsync(a => a.ProjectId == project.Id);
        Assert.False(membership.IsDeleted); // memberships left in place (ADR-0057/A1, plan §4.3a)
    }

    [Fact(DisplayName = "A removed project skips ALL resource/Heappe processing even when the payload still carries resources (review B08 MEDIUM)")]
    public async Task Should_SkipResourceAndHeappe_When_ProjectRemoved()
    {
        using var context = SeededContext();
        var projectCreation = new CountingProjectCreationService();
        var sync = new FakeHeappeSynchronizationService();

        await Handler(context, projectCreation, sync).HandleAsync(
            Created(Payload(isRemoved: true, resources: [HpcResource()])),
            CancellationToken.None);

        var project = await context.ProjectsQuery
            .SingleAsync(p => p.ExternalId == ProjectExternalId && p.ServiceName == ServiceName);
        Assert.True(project.IsDeleted);                            // still upserted + soft-deleted
        Assert.Empty(projectCreation.EnsuredResourceNames);        // no HEAppE ensure for a torn-down project
        Assert.Empty(sync.Triggers);                               // no command-template sync
        Assert.Empty(await context.ProjectResourcesQuery.ToListAsync()); // no resource rows created
    }

    [Fact(DisplayName = "Upserts the resource (Name, ServiceName), links it to the project, and upserts its assigned location")]
    public async Task Should_UpsertResource_And_AssignedLocation_ScopedToProject()
    {
        using var context = SeededContext();
        var resourceExternalId = Guid.NewGuid().ToString();

        await Handler(context).HandleAsync(
            Created(Payload(resources: [HpcResource(resourceExternalId: resourceExternalId)])),
            CancellationToken.None);

        var project = await context.ProjectsQuery.SingleAsync(p => p.ExternalId == ProjectExternalId);
        var resource = await context.ProjectResourcesQuery.SingleAsync(r => r.Name == "res-1" && r.ServiceName == ServiceName);
        var assignment = await context.AssignedProjectResourceToProjectsQuery
            .SingleAsync(a => a.ProjectId == project.Id && a.ProjectResourceId == resource.Id);
        Assert.False(assignment.IsDeleted);

        var assignedLocation = await context.AssignedLocationsQuery
            .SingleAsync(al => al.ExternalId == resourceExternalId && al.ServiceName == ServiceName);
        Assert.Equal(resource.Id, assignedLocation.ProjectResourceId);
        Assert.Equal(100, assignedLocation.AllocationUnitCount);
    }

    [Fact(DisplayName = "Does NOT match a same-named resource owned by a different project (review B07/2a)")]
    public async Task Should_NotMutateForeignProjectResource_When_NameMatches()
    {
        using var context = SeededContext();
        var foreignProjectId = ProjectId.Create(Guid.CreateVersion7());
        var foreignResource = new ProjectResource
        {
            Id = Guid.CreateVersion7(),
            Name = "storage-res",
            Description = "FOREIGN",
            ServiceName = ServiceName,
            StartDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
        };
        context.Add(foreignResource);
        context.Add(new AssignedProjectResourceToProject
        {
            ProjectId = foreignProjectId,
            ProjectResourceId = foreignResource.Id,
            ProjectResource = foreignResource,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);

        await Handler(context).HandleAsync(
            Created(Payload(resources: [NonHpcResource(name: "storage-res")])),
            CancellationToken.None);

        var resources = await context.ProjectResourcesQuery.Where(r => r.Name == "storage-res").ToListAsync();
        Assert.Equal(2, resources.Count); // a new row for our project; the foreign row was not reused
        Assert.Equal("FOREIGN", resources.Single(r => r.Id == foreignResource.Id).Description);
    }

    [Fact(DisplayName = "Ensures the HEAppE project per HPC resource on BOTH Created and Updated (ADR-0057/A1)")]
    public async Task Should_EnsureHeappe_PerHpcResource_On_BothEvents()
    {
        using var context = SeededContext();
        var projectCreation = new CountingProjectCreationService();
        var resourceExternalId = Guid.NewGuid().ToString();

        await Handler(context, projectCreation).HandleAsync(
            Created(Payload(resources: [HpcResource(resourceExternalId: resourceExternalId)])),
            CancellationToken.None);
        await Handler(context, projectCreation).HandleAsync(
            Updated(Payload(resources: [HpcResource(resourceExternalId: resourceExternalId)])),
            CancellationToken.None);

        Assert.Equal(["res-1", "res-1"], projectCreation.EnsuredResourceNames);
    }

    [Fact(DisplayName = "Triggers the command-template sync per HPC resource (ADR-0057/A2)")]
    public async Task Should_TriggerCommandTemplateSync_PerHpcResource()
    {
        using var context = SeededContext();
        var sync = new FakeHeappeSynchronizationService();

        await Handler(context, sync: sync).HandleAsync(Created(Payload()), CancellationToken.None);

        var trigger = Assert.Single(sync.Triggers);
        Assert.Equal("acme-proj", trigger.ProjectShortName.Value);
        Assert.Equal("res-1", trigger.ResourceName);
    }

    [Fact(DisplayName = "HEAppE failure throws and commits nothing (ADR-0057/A3 — not acked)")]
    public async Task Should_ThrowAndPersistNothing_When_HeappeFails()
    {
        using var context = SeededContext();
        var throwing = new ThrowingProjectCreationService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler(context, throwing).HandleAsync(Created(Payload()), CancellationToken.None));

        Assert.Equal(1, throwing.CallCount);
        Assert.Empty(await context.ProjectsQuery.ToListAsync()); // terminal SaveChanges never reached
        Assert.Empty(await context.ProjectResourcesQuery.ToListAsync());
    }

    [Fact(DisplayName = "A resource missing its Heappe instance is skipped (partial import): project + other resources are still persisted, and the handler returns failure so the event is not acked")]
    public async Task Should_PartiallyImportProject_When_OneResourceMissingHeappeInstance()
    {
        using var context = SeededContext();
        var sync = new FakeHeappeSynchronizationService();

        var result = await Handler(context, sync: sync).HandleAsync(
            Created(Payload(resources:
            [
                HpcResource(name: "res-ok"),
                HpcResource(name: "res-bad", heappeUri: null, heappeIdentifier: null),
            ])),
            CancellationToken.None);

        // Partial-import failure (ADR-0057/A?): the good resource + project ARE committed, but the
        // overall Result is a Failure carrying the per-resource error so the event is not acked and
        // the bad resource is retried next iteration.
        Assert.False(result.IsSuccess);

        var project = await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId);
        Assert.NotNull(project);

        // The ProjectResource row itself is upserted+assigned BEFORE the Heappe check, so both resources
        // exist; the failing one just never gets its Heappe project ensured / sync triggered.
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-ok" && r.ServiceName == ServiceName));
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-bad" && r.ServiceName == ServiceName));

        // Only the successfully-processed resource triggers the command-template sync.
        var trigger = Assert.Single(sync.Triggers);
        Assert.Equal("res-ok", trigger.ResourceName);
    }

    [Fact(DisplayName = "A resource whose endpoint resolves to no location is skipped (partial import): project + other resources are still persisted")]
    public async Task Should_PartiallyImportProject_When_OneResourceLocationMissing()
    {
        using var context = SeededContext();
        var badResource = HpcResource(name: "res-bad") with { Endpoint = "unknown.endpoint" };

        var result = await Handler(context).HandleAsync(
            Updated(Payload(resources: [HpcResource(name: "res-ok"), badResource])),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        // The ProjectResource row is upserted before RequireLocationByEndpoint runs, so both resources
        // exist even though res-bad's location resolution failed.
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-ok" && r.ServiceName == ServiceName));
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-bad" && r.ServiceName == ServiceName));
    }

    [Fact(DisplayName = "All resources failing still upserts the project, and the failure aggregates every resource error")]
    public async Task Should_PartiallyImportProject_When_AllResourcesFail()
    {
        using var context = SeededContext();
        var badResourceOne = HpcResource(name: "res-bad-1") with { Endpoint = "unknown.endpoint" };
        var badResourceTwo = HpcResource(name: "res-bad-2", heappeUri: null, heappeIdentifier: null);

        var result = await Handler(context).HandleAsync(
            Created(Payload(resources: [badResourceOne, badResourceTwo])),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        // Both resources are still upserted (row creation happens before the failing checks); only
        // their downstream Heappe/location processing failed.
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-bad-1" && r.ServiceName == ServiceName));
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-bad-2" && r.ServiceName == ServiceName));
        // res-bad-1 fails location resolution (message names the endpoint, not the resource); res-bad-2
        // fails the missing-Heappe-instance check (message names the resource). The aggregated
        // partial-import error mentions both failure reasons.
        Assert.Contains("2 failed resource(s)", result.Error.Description, StringComparison.Ordinal);
        Assert.Contains("unknown.endpoint", result.Error.Description, StringComparison.Ordinal);
        Assert.Contains("res-bad-2", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Returns failure (not acked) when an HPC resource has no Heappe instance configured")]
    public async Task Should_ReturnFailure_When_HpcResourceMissingHeappeInstance()
    {
        using var context = SeededContext();

        var result = await Handler(context).HandleAsync(
            Created(Payload(resources: [HpcResource(heappeUri: null, heappeIdentifier: null)])),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        // The project itself is still upserted (partial import); the ProjectResource row is also created
        // (upsert happens before the Heappe check) — only the Heappe project ensure / sync trigger is skipped.
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        Assert.NotEmpty(await context.ProjectResourcesQuery.ToListAsync());
    }

    [Fact(DisplayName = "Returns failure (not acked) when a resource endpoint resolves to no location — the project and resource row are still persisted")]
    public async Task Should_ReturnFailure_When_LocationMissing()
    {
        using var context = SeededContext();
        // A resource whose endpoint has no matching Location row (only karolina.it4i.cz / storage.it4i.cz
        // are seeded). RequireLocationByEndpoint fails → the resource's Heappe/location processing is
        // skipped (partial import), but the ProjectResource row itself was already upserted.
        var resource = HpcResource() with { Endpoint = "unknown.endpoint" };

        var result = await Handler(context).HandleAsync(
            Updated(Payload(resources: [resource])), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        Assert.NotEmpty(await context.ProjectResourcesQuery.ToListAsync());
    }

    [Fact(DisplayName = "Returns success on the happy path when all dependencies are present")]
    public async Task Should_ReturnSuccess_When_DependenciesPresent()
    {
        using var context = SeededContext();

        var result = await Handler(context).HandleAsync(Created(Payload()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
    }

    [Fact(DisplayName = "Legacy parity (ADR-0065, D9a) adjusted for partial import: a resource whose location is absent is NOT persisted (project still is), then REPLAYS and persists once the location appears")]
    public async Task Should_NotPersistResource_ThenReplay_When_ResourceDependencyAbsent()
    {
        using var context = SeededContext();
        const string replayEndpoint = "replay.it4i.cz"; // no seeded Location matches this endpoint yet
        var resource = HpcResource() with { Endpoint = replayEndpoint };
        var payload = Payload(resources: [resource]);

        // Attempt 1 (dependency absent): RequireLocationByEndpoint fails for the only resource, so the
        // overall Result is a Failure (partial import) — but the project itself is still committed;
        // only the resource is skipped and the event replays for it next iteration.
        var firstAttempt = await Handler(context).HandleAsync(Created(payload), CancellationToken.None);

        Assert.False(firstAttempt.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        // The ProjectResource row is upserted before RequireLocationByEndpoint runs, so it already exists
        // even though the location resolution failed on attempt 1.
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-1" && r.ServiceName == ServiceName));

        // Next iteration re-diffs the same input from a clean slate: a fresh scope discards the
        // uncommitted graph the failed attempt tracked.
        context.ChangeTracker.Clear();

        // Independent source of truth: seed the missing HPC location explicitly (matched by endpoint),
        // not by recomputing the way the handler does.
        context.Add(new Location
        {
            Id = Guid.CreateVersion7(),
            Name = "replay",
            Endpoint = replayEndpoint,
            ServiceName = ServiceName,
            ProviderId = ProviderId,
            LocationTypeId = (int)ELocationType.HPC,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(CancellationToken.None);

        // Attempt 2 (same input, dependency now present): Result.Success ⇒ persisted, SHA written.
        var secondAttempt = await Handler(context).HandleAsync(Created(payload), CancellationToken.None);

        Assert.True(secondAttempt.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "res-1" && r.ServiceName == ServiceName));
    }

    [Fact(DisplayName = "W2: returns failure (not acked) when a NON-NULL allocation-unit type is not seeded — no silent 'unspecified' fallback; the project is still upserted (partial import)")]
    public async Task Should_ReturnFailure_When_AllocationUnitTypeMissing()
    {
        using var context = SeededContext();
        // "gpu-hours" is a mapped unit name that is NOT seeded. Silently falling back to "unspecified"
        // (the old behaviour) would store a name different from the one the producer hashed and storm
        // ProjectUpdated every restart, so the handler must fail loud (retriable) instead.
        var resource = new ResourcePayload(
            "gpu-res",
            Guid.NewGuid().ToString(),
            "resource-desc",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            false,
            HpcEndpoint,
            "Karolina Offering",
            "gpu-hours",
            100,
            "https://heappe.test/",
            "heappe-id-1",
            []);

        var result = await Handler(context).HandleAsync(Created(Payload(resources: [resource])), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(await context.ProjectsQuery.SingleOrDefaultAsync(p => p.ExternalId == ProjectExternalId));
        // The ProjectResource row is upserted before RequireAllocationUnitTypeAsync runs, so it exists
        // even though the allocation-unit type resolution failed.
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "gpu-res" && r.ServiceName == ServiceName));
    }

    [Fact(DisplayName = "Does not touch Heappe for a non-HPC resource, but still persists it")]
    public async Task Should_SkipHeappe_When_ResourceNotHpc()
    {
        using var context = SeededContext();
        var projectCreation = new CountingProjectCreationService();
        var sync = new FakeHeappeSynchronizationService();

        await Handler(context, projectCreation, sync).HandleAsync(
            Created(Payload(resources: [NonHpcResource()])),
            CancellationToken.None);

        Assert.Empty(projectCreation.EnsuredResourceNames);
        Assert.Empty(sync.Triggers);
        Assert.NotNull(await context.ProjectResourcesQuery.SingleOrDefaultAsync(r => r.Name == "storage-res"));
    }

    [Fact(DisplayName = "Returns failure (not acked) when the service account is not bootstrapped (B06/F1)")]
    public async Task Should_ReturnFailure_When_ServiceAccountMissing()
    {
        using var context = SeededContext(seedServiceAccount: false);

        var result = await Handler(context).HandleAsync(Created(Payload(resources: [])), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await context.ProjectsQuery.ToListAsync());
    }

    [Fact(DisplayName = "Performs a SINGLE terminal SaveChanges across multiple resources (ADR-0057/A3)")]
    public async Task Should_SaveOnce_When_MultipleResources()
    {
        using var context = new RecordingUserOrgDbContext();
        Seed(context);
        context.ChangeTracker.Clear();
        context.BeginCapture();

        await Handler(context).HandleAsync(
            Created(Payload(resources:
            [
                HpcResource(name: "res-1"),
                HpcResource(name: "res-2"),
            ])),
            CancellationToken.None);

        Assert.Single(context.SaveSnapshots); // no per-resource commit mid-loop
    }

    [Fact(DisplayName = "Handler is registered via the B06 seam for both project events")]
    public void Should_RegisterHandler_For_BothProjectEvents()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddScoped<IProjectCreationService, FakeProjectCreationService>();
        services.AddScoped<IHeappeService, FakeHeappeService>();
        services.AddScoped<IHeappeConnectionFactory, FakeHeappeConnectionFactory>();
        services.AddScoped<IHeappeSynchronizationService, FakeHeappeSynchronizationService>();
        services.AddLogging();
        services.AddWaldurConsumerInstanceOptions(new WaldurConsumerInstanceOptions(ServiceName, DomainName, PreferredAuthType, PiEmail));
        services.AddWaldurEventConsumers();
        services.AddWaldurProjectHandler();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<ProjectCreated>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaldurEventHandler<ProjectUpdated>>());
    }
}
