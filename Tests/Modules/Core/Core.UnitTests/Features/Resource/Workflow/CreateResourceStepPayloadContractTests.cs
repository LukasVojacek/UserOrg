using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Core.Domain.Shared;
using Core.Features.Resource.Workflow;
using Core.Features.Resource.Workflow.Steps;

using Moq;

using WorkflowEngine.Contracts.Abstractions;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Features.Resource.Workflow;

/// <summary>
/// The create-resource flow's step payloads are a PERSISTED contract, not an in-process message: the engine
/// writes every step message into <c>RequestSteps.InputData</c> with
/// <c>JsonSerializer.SerializeToDocument(message, message.GetType())</c> (RequestWorker.cs:121,
/// SubmitHandler.cs:85) and reads it back with <c>JsonSerializer.Deserialize(raw, type)</c> after a crash or a
/// scheduled retry (RequestWorkerStartupRecovery.cs:101/192, RetryScheduledScanner.cs:114). A payload that does
/// not survive that round trip cannot be recovered, cannot be retried, and — because the four create-resource
/// steps carry the live <c>Provider</c> aggregate — is not even writable in the first place.
/// <para>
/// Every step here is produced by PRODUCTION code: the endpoint builds the initial one
/// (<see cref="CreateResourceFlowStepFactory"/>) and <see cref="CreateStrategy.Transition"/> derives the rest.
/// No test constructs a step record literally, so these tests compile against both the current shape and the
/// natural-key shape T-055-04 introduces, and they fail on behaviour rather than on a rewritten constructor.
/// </para>
/// </summary>
public sealed class CreateResourceStepPayloadContractTests
{
    /// <summary>The five step records of the create-resource flow, in the order the strategy walks them.</summary>
    private static readonly Type[] FlowStepTypes =
    [
        typeof(CheckHeappeStep),
        typeof(InitializeProjectResourceStep),
        typeof(InitializeHeappeStep),
        typeof(FinalizeCreationProjectResourceStep),
        typeof(SendEmailStep),
    ];

    // ── The round trip the engine performs on every step ──────────────────────────────────────────────

    [Fact]
    public async Task Should_RoundTripTheCheckHeappeStep_When_TheEngineSerialisesAndReplaysIt()
        => AssertRoundTripsThroughTheEngine((await FlowAsync())[0]);

    [Fact]
    public async Task Should_RoundTripTheInitializeProjectResourceStep_When_TheEngineSerialisesAndReplaysIt()
        => AssertRoundTripsThroughTheEngine((await FlowAsync())[1]);

    [Fact]
    public async Task Should_RoundTripTheInitializeHeappeStep_When_TheEngineSerialisesAndReplaysIt()
        => AssertRoundTripsThroughTheEngine((await FlowAsync())[2]);

    [Fact]
    public async Task Should_RoundTripTheFinalizeCreationProjectResourceStep_When_TheEngineSerialisesAndReplaysIt()
        => AssertRoundTripsThroughTheEngine((await FlowAsync())[3]);

    /// <summary>
    /// Characterization: the flow's last step already carries nothing but primitives and a
    /// <see cref="JsonDocument"/>, so it round-trips today. It is the shape the other four have to reach, and
    /// this test is the tripwire that says so if the change breaks the one step that was already correct.
    /// </summary>
    [Fact]
    public async Task Should_RoundTripTheSendEmailStep_When_TheEngineSerialisesAndReplaysIt()
        => AssertRoundTripsThroughTheEngine((await FlowAsync())[4]);

    // ── What the payload must carry instead of the aggregate ──────────────────────────────────────────

    [Fact]
    public async Task Should_CarryTheProviderNaturalKey_When_TheEndpointBuildsTheInitialStep()
    {
        var step = await CreateResourceFlowStepFactory.InitialStepAsync(CreateResourceFlowStepFactory.ProviderWith());

        using var payload = JsonSerializer.SerializeToDocument(step, step.GetType());
        var root = payload.RootElement;

        // ADR-0029: the natural key crosses the boundary, typed — not the aggregate that owns it.
        Assert.True(
            root.TryGetProperty("ProviderName", out var providerName),
            $"The step payload has no ProviderName property. It carries: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}.");
        Assert.Equal(JsonValueKind.String, providerName.ValueKind);
        Assert.Equal(CreateResourceFlowStepFactory.ProviderNameValue, providerName.GetString());

        Assert.False(
            root.TryGetProperty("Provider", out _),
            "The step payload still carries the Provider aggregate.");
    }

    [Fact]
    public async Task Should_CarryLocationNameAndEndpointPerResolvedLocation_When_TheEndpointBuildsTheInitialStep()
    {
        // The two fields CheckHeappeHandler actually reads off a resolved location (CheckHeappeHandler.cs:35-46):
        // the endpoint it probes, and the name it puts in the HEAPPE_UNREACHABLE report.
        var step = await CreateResourceFlowStepFactory.InitialStepAsync(CreateResourceFlowStepFactory.ProviderWith());

        using var payload = JsonSerializer.SerializeToDocument(step, step.GetType());
        var refs = ResolvedLocationRefs(payload.RootElement);

        var single = Assert.Single(refs);
        Assert.Equal(CreateResourceFlowStepFactory.LocationNameValue, PropertyOrNull(single, "LocationName"));
        Assert.Equal(CreateResourceFlowStepFactory.LocationEndpoint, PropertyOrNull(single, "Endpoint"));
    }

    [Fact]
    public async Task Should_CarryOneRefPerAssignment_When_TheSameLocationIsAssignedTwice()
    {
        // Same location, two aggregations — two legitimate allocations (ADR-0050 §3). Both resolve to the same
        // location, and both must survive into the payload: the liveness gate reports per resolved location.
        var request = CreateResourceFlowStepFactory.RequestFor(
            CreateResourceFlowStepFactory.Assignment(aggregationName: "agg-a"),
            CreateResourceFlowStepFactory.Assignment(aggregationName: "agg-b"));

        var step = await CreateResourceFlowStepFactory.InitialStepAsync(CreateResourceFlowStepFactory.ProviderWith(), request);

        using var payload = JsonSerializer.SerializeToDocument(step, step.GetType());
        var refs = ResolvedLocationRefs(payload.RootElement);

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal(CreateResourceFlowStepFactory.LocationNameValue, PropertyOrNull(r, "LocationName")));
        Assert.All(refs, r => Assert.Equal(CreateResourceFlowStepFactory.LocationEndpoint, PropertyOrNull(r, "Endpoint")));
    }

    [Fact]
    public void Should_CarryNoDomainEntity_When_TheStepRecordsAreDeclared()
    {
        // The rule AssignResourceFlowSteps.cs:7 states and this flow breaks: "Each carries only the projection
        // slug — handlers re-read needed data." Value objects (ProviderName, ResourceName …) are fine and are
        // the point; entities and aggregate roots are not, and neither is a ValueTuple, which
        // System.Text.Json serialises as {} because tuples expose fields rather than properties.
        var offenders = FlowStepTypes
            .SelectMany(stepType => stepType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(property => CarriedTypes(property.PropertyType)
                    .Where(IsForbiddenPayloadType)
                    .Select(carried => $"{stepType.Name}.{property.Name} carries {carried.Name}")))
            .ToArray();

        Assert.Empty(offenders);
    }

    // ── The persisted identifiers that must NOT move ──────────────────────────────────────────────────

    [Fact]
    public async Task Should_KeepTheStateNames_When_ThePayloadShapeChanges()
    {
        // CurrentState / StepName are persisted strings on the request aggregate. Renaming one orphans every
        // in-flight row, including the ones that CAN still be deserialised today.
        var flow = await FlowAsync();

        Assert.Equal(
            ["CheckHeappe", "InitializeProjectResource", "InitializeHeappe", "FinalizeCreationProjectResource", "SendEmail"],
            flow.Select(step => step.StateName).ToArray());
    }

    [Fact]
    public void Should_KeepTheClrTypeNames_When_ThePayloadShapeChanges()
    {
        // RequestSteps.MessageType stores AssemblyQualifiedName and recovery resolves it with Type.GetType
        // (RequestWorkerStartupRecovery.cs:88/230, RetryScheduledScanner.cs:98). Namespace, type name and
        // assembly are therefore all part of the persisted contract.
        Assert.Equal(
            [
                "Core.Features.Resource.Workflow.Steps.CheckHeappeStep",
                "Core.Features.Resource.Workflow.Steps.InitializeProjectResourceStep",
                "Core.Features.Resource.Workflow.Steps.InitializeHeappeStep",
                "Core.Features.Resource.Workflow.Steps.FinalizeCreationProjectResourceStep",
                "Core.Features.Resource.Workflow.Steps.SendEmailStep",
            ],
            FlowStepTypes.Select(type => type.FullName).ToArray());

        Assert.All(FlowStepTypes, type => Assert.Equal("Core", type.Assembly.GetName().Name));
        Assert.All(FlowStepTypes, type => Assert.NotNull(Type.GetType(type.AssemblyQualifiedName!)));
    }

    // ── The old shape must fail loudly, never silently ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OldShapePayloads))]
    public void Should_FailLoudly_When_AnOldShapePayloadIsReplayed(string stepTypeName, string oldPayload)
    {
        // Verbatim output of today's JsonSerializer.SerializeToDocument for these steps (captured from the
        // running code), i.e. exactly what sits in RequestSteps.InputData for any row written before the change:
        // the Provider aggregate inline, no ProviderName anywhere, ResolvedLocations flattened to [{}].
        //
        // ProviderName must be REQUIRED so replaying such a row throws instead of quietly producing a step whose
        // provider is default(ProviderName) — a silent default here would send the handlers looking up a
        // provider named null and report the resulting failure as a domain error rather than as the
        // unrecoverable payload it is.
        var stepType = FlowStepTypes.Single(type => type.Name == stepTypeName);

        var exception = Record.Exception(() => JsonSerializer.Deserialize(oldPayload, stepType));

        Assert.IsType<JsonException>(exception, exactMatch: false);
    }

    // ── The aggregate must not leave the HTTP scope ───────────────────────────────────────────────────

    [Fact]
    public async Task Should_NotLetTheProviderAggregateLeaveTheHttpScope_When_TheEndpointSubmitsTheFlow()
    {
        // owningProvider stays for the 404 (unknown location) and the aggregate-level checks; it must not be
        // reachable from the submitted step. Reference identity, not shape: this is what makes the worker's
        // scope re-read the provider instead of provisioning over a graph from a disposed DbContext (T-055-01).
        var provider = CreateResourceFlowStepFactory.ProviderWith();
        var step = await CreateResourceFlowStepFactory.InitialStepAsync(provider);

        var escaped = new HashSet<object>(ReachableObjects(step), ReferenceEqualityComparer.Instance);

        Assert.DoesNotContain(provider, escaped, ReferenceEqualityComparer.Instance);
        Assert.All(provider.Locations, location => Assert.DoesNotContain(location, escaped, ReferenceEqualityComparer.Instance));
    }

    // The other half of that rule — dropping the aggregate from the payload must not drop the endpoint's
    // per-location existence check (ADR-0050 §4) — is already pinned by
    // CreateResourceOnLocationEndpointTests.Should_ReturnNotFoundNamingLocationAndNotSubmit_When_LocationUnknown,
    // which names no payload member and therefore survives this change untouched.

    public static TheoryData<string, string> OldShapePayloads()
    {
        const string ProviderAggregateJson =
            """{"Name":"acme","Description":null,"SupportEmail":null,"ExternalId":null,"Mode":0,"ServiceName":"LEXIS","IsPrivate":false,"CountryId":1,"IsDeleted":false,"Locations":[{"Name":"location-1","Description":null,"Endpoint":"https://location-1.heappe.example","IsPrivate":false,"IsShared":true,"IsDeleted":false,"Mode":0,"AssignedLocations":[]}],"DomainEvents":[]}""";
        const string RequestJson =
            """{"Name":"res-1","Description":"desc","PrimaryInvestigatorEmail":"pi@example.com","StartDate":"2026-01-01T00:00:00+00:00","EndDate":"2026-12-31T00:00:00+00:00","AccountString":"ACC-1","UnitType":2,"MappingOneToOne":true,"UseAccountingStringForScheduler":false,"Clusters":[{"ClusterName":"cluster-1","ScratchPath":"/scratch","ProjectPath":"/project"}],"Username":"user@example.com","LicenseUrl":null,"AssignedLocations":[{"LocationName":"location-1","AllocationUnitType":2,"AggregationName":"agg","AllocationUnitCount":100,"AllocationDescription":"alloc","PreferredAuthenticationType":"Password","Specifications":[{"Name":"HeappeUri","Value":"https://location-1.heappe.example"}]}]}""";
        const string AssignedLocationsJson =
            """[{"LocationName":"location-1","AllocationUnitType":2,"AggregationName":"agg","AllocationUnitCount":100,"AllocationDescription":"alloc","PreferredAuthenticationType":"Password","Specifications":[{"Name":"HeappeUri","Value":"https://location-1.heappe.example"}]}]""";
        const string RequestId = "01a08175-03b2-77ee-9755-6a08780fd4b6";

        return new TheoryData<string, string>
        {
            {
                nameof(CheckHeappeStep),
                $$"""{"Provider":{{ProviderAggregateJson}},"Data":{{RequestJson}},"ResolvedLocations":[{}],"RequestId":"{{RequestId}}","StateName":"CheckHeappe"}"""
            },
            {
                nameof(InitializeProjectResourceStep),
                $$"""{"Provider":{{ProviderAggregateJson}},"Data":{{RequestJson}},"RequestId":"{{RequestId}}","StateName":"InitializeProjectResource"}"""
            },
            {
                nameof(InitializeHeappeStep),
                $$"""{"Provider":{{ProviderAggregateJson}},"Data":{{RequestJson}},"RequestId":"{{RequestId}}","StateName":"InitializeHeappe"}"""
            },
            {
                nameof(FinalizeCreationProjectResourceStep),
                $$"""{"Provider":{{ProviderAggregateJson}},"Name":"res-1","PrimaryInvestigatorEmail":"pi@example.com","AssignedLocations":{{AssignedLocationsJson}},"Username":"user@example.com","Data":null,"RequestId":"{{RequestId}}","StateName":"FinalizeCreationProjectResource"}"""
            },
        };
    }

    /// <summary>
    /// The whole flow as production produces it: the endpoint builds the first step,
    /// <see cref="CreateStrategy.Transition"/> derives the other four. The HEAppE result fed into the
    /// InitializeHeappe → Finalize transition is the shape <c>InitializeHeappeHandler</c> really returns
    /// (InitializeHeappeHandler.cs:144-149).
    /// </summary>
    private static async Task<IReadOnlyList<StepRequest>> FlowAsync()
    {
        var strategy = new CreateStrategy();
        var check = await CreateResourceFlowStepFactory.InitialStepAsync(CreateResourceFlowStepFactory.ProviderWith());

        var initializeResource = strategy.Transition(check, new StepResult(null), null!)!;
        var initializeHeappe = strategy.Transition(initializeResource, new StepResult(null), null!)!;

        var heappeResult = new StepResult(JsonDocument.Parse(
            $$"""[{"locationName":"{{CreateResourceFlowStepFactory.LocationNameValue}}","projectId":7,"publicKey":"ssh-key"}]"""));
        var finalize = strategy.Transition(initializeHeappe, heappeResult, null!)!;

        var sendEmail = strategy.Transition(finalize, new StepResult(null), null!)!;

        return [check, initializeResource, initializeHeappe, finalize, sendEmail];
    }

    private static void AssertRoundTripsThroughTheEngine(StepRequest step)
        => CreateResourceFlowStepFactory.AssertEngineRoundTrip(step);

    /// <summary>
    /// The step's resolved locations, read WITHOUT naming the member: acceptance fixes the element shape
    /// (LocationName + Endpoint) but not whether the member is called <c>ResolvedLocations</c> or
    /// <c>Locations</c>, and this contract does not depend on which is chosen. The step payload has exactly one
    /// top-level array.
    /// </summary>
    private static IReadOnlyList<JsonElement> ResolvedLocationRefs(JsonElement root)
    {
        var arrays = root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Array)
            .ToArray();

        var array = Assert.Single(arrays);
        return [.. array.Value.EnumerateArray()];
    }

    private static string? PropertyOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The declared type plus, for collections, whatever it carries.</summary>
    private static IEnumerable<Type> CarriedTypes(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in CarriedTypes(argument))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsForbiddenPayloadType(Type type) =>
        typeof(BaseAuditableEntity).IsAssignableFrom(type)
        || typeof(AggregateRoot).IsAssignableFrom(type)
        || typeof(ITuple).IsAssignableFrom(type);

    /// <summary>
    /// Every REFERENCE-typed object reachable from the step through public properties and enumerables.
    /// <para>
    /// Value types are not followed: reference identity is meaningless for them, every getter hands back a
    /// fresh box, and following them does not terminate (<c>DateTimeOffset.Date.Date.Date…</c>). Property
    /// getters are invoked defensively — on a lean-loaded aggregate some of them throw
    /// (<c>AssignedLocation.ResourceKey</c>), and a throwing getter is not what this walk is asking about.
    /// </para>
    /// </summary>
    private static IEnumerable<object> ReachableObjects(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>([root]);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;

            if (current is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    Push(pending, item);
                }

                continue;
            }

            foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.PropertyType.IsValueType)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch (Exception)
                {
                    continue;
                }

                Push(pending, value);
            }
        }
    }

    private static void Push(Stack<object> pending, object? candidate)
    {
        if (candidate is not null and not string && !candidate.GetType().IsValueType)
        {
            pending.Push(candidate);
        }
    }
}
