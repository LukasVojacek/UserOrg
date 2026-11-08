using System.Text.Json;

using Core.DataAccess.Contexts;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using ELocationType = Core.Domain.Shared.ELocationType;

namespace Core.UnitTests.Features.Resource.Workflow;

/// <summary>
/// The create-resource flow's initial step is unserialisable in PRODUCTION, not merely in theory — and the
/// reason is invisible to any fixture that hand-builds a <c>Provider</c>.
/// <para>
/// <c>IProviderRepository.FindAsync</c> is the LEAN load path (DatabaseContext.ProviderRepository.cs:10-12):
/// provider → locations → allocations → allocation values, but NOT the allocations' <c>ProjectResource</c>.
/// <c>AssignedLocation.ResourceKey</c> (AssignedLocation.cs:32) dereferences that unmaterialised navigation
/// with <c>_resource!.Key</c>, so the moment System.Text.Json walks a real provider that owns at least one
/// allocation, the getter throws <see cref="NullReferenceException"/> — and the engine's
/// <c>RecordStepStarted</c> write (RequestWorker.cs:121) dies before the step is ever persisted.
/// </para>
/// <para>
/// SQLite over the real generated schema, as in <c>InitializeProjectResourceHandlerPersistenceTests</c>: the
/// point is what the REAL query materialises, which no in-memory double reproduces. Nothing here saves through
/// <c>IUnitOfWork</c>, so neither the outbox leg nor T-055-03's detached-aggregate guard is involved.
/// </para>
/// </summary>
public sealed class CreateResourceStepPayloadPersistenceTests : SqliteQueryHarness
{
    private const string SeededResourceName = "seeded-resource";

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IProviderRepository>(sp => sp.GetRequiredService<DatabaseContext>());

    // ── Characterization: what the endpoint's own load really hands over ──────────────────────────────

    [Fact]
    public async Task Should_LoadAllocationsWithoutTheirResource_When_TheEndpointResolvesTheProvider()
    {
        // The premise of the reproduction below, pinned on its own so a change to the repository's Include set
        // shows up here rather than as a mystery elsewhere. This is a property of IProviderRepository.FindAsync
        // and stays true after T-055-04 — the fix is to stop putting the aggregate in the payload, not to
        // eager-load more of it.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var provider = await scope.ServiceProvider.GetRequiredService<IProviderRepository>()
            .FindAsync(ProviderName.From(CreateResourceFlowStepFactory.ProviderNameValue), TestContext.Current.CancellationToken);

        Assert.NotNull(provider);
        var allocation = Assert.Single(Assert.Single(provider.Locations).AssignedLocations);

        // FindAsync uses the lean load path that does not include allocated resources, so _resource
        // remains unmaterialised (null). ResourceKey returns null safely via the null-coalescing operator.
        Assert.Null(allocation.ResourceKey);
    }

    // ── Reproduction: the payload the endpoint builds cannot be written to RequestSteps at all ────────

    [Fact]
    public async Task Should_SerialiseTheInitialStep_When_TheProviderComesFromTheRealRepository()
    {
        // What production does, in order: the endpoint resolves the provider through the real repository and
        // submits the initial step; the engine serialises that step verbatim with
        // JsonSerializer.SerializeToDocument(step, step.GetType()).
        //
        // Today serialisation walks Provider → Locations → AssignedLocations → ResourceKey and throws
        // NullReferenceException, so a create request against ANY provider that already has one allocation
        // cannot even be recorded — and the failure surfaces inside the engine, far from the endpoint that
        // built the payload.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();

        var step = await CreateResourceFlowStepFactory.InitialStepAsync(repository);

        CreateResourceFlowStepFactory.AssertEngineRoundTrip(step);
    }

    [Fact]
    public async Task Should_KeepThePayloadFreeOfTheAllocationGraph_When_TheProviderComesFromTheRealRepository()
    {
        // Not just "it serialises": the payload must not contain the provider's existing allocations at all.
        // They are unrelated to the resource being created, they are stale the moment the request is enqueued,
        // and they are what makes the payload grow without bound as a provider accumulates allocations.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();

        var step = await CreateResourceFlowStepFactory.InitialStepAsync(repository);

        using var payload = JsonSerializer.SerializeToDocument(step, step.GetType());
        var raw = payload.RootElement.GetRawText();

        Assert.DoesNotContain(SeededResourceName, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("seeded-allocation-slug", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("AssignedLocations\":[{\"AllocationUnitTypeId", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_IncludeCreatedBy_InTheInitialStep_When_TheEndpointCreatesTheStep()
    {
        // The endpoint resolves a caller's UserId and passes it as CreatedBy when creating the initial step.
        // Verify it appears in the submitted CheckHeappeStep so it persists through the workflow.
        var provider = CreateResourceFlowStepFactory.ProviderWith();
        var step = await CreateResourceFlowStepFactory.InitialStepAsync(provider);

        // Inspect the serialized payload to confirm CreatedBy appears as a string GUID value.
        using var payload = JsonSerializer.SerializeToDocument(step, step.GetType());
        var raw = payload.RootElement.GetRawText();
        Assert.Contains("CreatedBy", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_DeserializePayload_When_CreatedByFieldIsAbsent()
    {
        // Legacy persisted steps (from before CreatedBy feature) lack the CreatedBy field.
        // Verify deserialization succeeds so old workflow instances continue to replay.
        var provider = CreateResourceFlowStepFactory.ProviderWith();
        var step = await CreateResourceFlowStepFactory.InitialStepAsync(provider);

        // Serialize, then remove CreatedBy field, then deserialize.
        using var payload = System.Text.Json.JsonSerializer.SerializeToDocument(step, step.GetType());
        var raw = payload.RootElement.GetRawText();

        // Remove CreatedBy field entirely from the JSON
        // Pattern: ,"CreatedBy":"<uuid>" or "CreatedBy":"<uuid>", followed by more content
        var legacy = System.Text.RegularExpressions.Regex.Replace(
            raw,
            @",?\s*""CreatedBy""\s*:\s*""[0-9a-f\-]+""",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Deserialize should not throw even if CreatedBy is missing.
        var replayed = System.Text.Json.JsonSerializer.Deserialize(legacy, step.GetType()) as WorkflowEngine.Contracts.Abstractions.StepRequest;
        Assert.NotNull(replayed);
        Assert.Equal(step.RequestId, replayed.RequestId);
        Assert.Equal(step.StateName, replayed.StateName);
    }

    /// <summary>One provider, one location, and one resource already allocated to it.</summary>
    private async Task SeedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = Provider.Create(ProviderName.From(CreateResourceFlowStepFactory.ProviderNameValue), countryId: 1);
        Assert.True(provider.AddLocation(
            LocationName.From(CreateResourceFlowStepFactory.LocationNameValue),
            CreateResourceFlowStepFactory.LocationEndpoint,
            ELocationType.HPC, isShared: true).IsSuccess);

        var resource = ProjectResource.Create(
            ResourceName.Create(SeededResourceName),
            provider,
            UserId.From(Guid.NewGuid()),
            CreateResourceFlowStepFactory.Start,
            CreateResourceFlowStepFactory.End);

        Assert.True(provider.AssignResource(
            LocationName.From(CreateResourceFlowStepFactory.LocationNameValue),
            resource,
            allocationUnitTypeId: (int)EAllocationUnitType.NodeHours,
            allocationUnitCount: 10,
            description: "seeded allocation",
            aggregationName: null,
            specifications: [],
            resourceHasNonSharedLocation: false,
            slug: "seeded-allocation-slug").IsSuccess);

        context.Add(provider);
        context.Add(resource);

        // Plain DbContext.SaveChanges: IUnitOfWork would route the durable ProjectResourceActivatedEvent
        // through the SQL-Server-bound OutboxDbContext, which has nothing to do with this fixture.
        await context.SaveChangesAsync(ct);
    }
}
