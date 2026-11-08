using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Steps;
using Core.UnitTests.Shared;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

using ELocationType = Core.Domain.Shared.ELocationType;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Features.Resource.Workflow;

/// <summary>
/// Builds the create-resource flow's initial step by running the REAL endpoint
/// (<see cref="CreateResourceOnLocationEndpoint.HandleAsync"/>) and capturing what it hands to
/// <see cref="IRequestApplicationService.SubmitAsync"/>.
/// <para>
/// T-055-04 changes the shape of <see cref="CheckHeappeStep"/> and its three successors. A test that
/// constructs those records literally has to be rewritten by the implement phase before it can even compile,
/// which makes it useless as a red test. Going through the endpoint means the tests name no constructor and
/// no payload member: they compile against both the old and the new shape and fail on BEHAVIOUR, which is the
/// only thing that can be red before the production change and green after it.
/// </para>
/// <para>
/// It is also the more honest fixture: the step under test is the one production actually builds, from a
/// provider aggregate that production actually resolved, rather than a hand-assembled approximation.
/// </para>
/// </summary>
internal static class CreateResourceFlowStepFactory
{
    internal const string ProviderNameValue = "acme";
    internal const string LocationNameValue = "location-1";
    internal const string LocationEndpoint = "https://location-1.heappe.example";
    internal const string OtherLocationNameValue = "location-2";
    internal const string OtherLocationEndpoint = "https://location-2.heappe.example";
    internal const string DefaultHeappeIdentifier = "heappe-1";
    internal const string ResourceNameValue = "res-1";
    internal const string PrimaryInvestigatorEmail = "pi@example.com";
    internal const string UsernameValue = "user@example.com";

    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The service-key caller that owns <see cref="ProviderNameValue"/>.</summary>
    internal static Caller OwningCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(ProviderNameValue), IsServiceKeyIdentity: true);

    internal static LocationAssignment Assignment(
        string locationName = LocationNameValue,
        EAllocationUnitType allocationUnitType = EAllocationUnitType.NodeHours,
        string? aggregationName = "agg",
        IReadOnlyList<SpecificationItem>? specifications = null) => new(
            LocationName: locationName,
            AllocationUnitType: allocationUnitType,
            AggregationName: aggregationName,
            AllocationUnitCount: 100,
            AllocationDescription: "alloc",
            PreferredAuthenticationType: "Password",
            Specifications: specifications ?? [new SpecificationItem("HeappeUri", LocationEndpoint), new SpecificationItem("HEAPPE_IDENTIFIER", DefaultHeappeIdentifier)]);

    internal static Request RequestFor(params LocationAssignment[] assignments) => new(
        Name: ResourceNameValue,
        Description: "desc",
        PrimaryInvestigatorEmail: PrimaryInvestigatorEmail,
        StartDate: Start,
        EndDate: End,
        AccountString: "ACC-1",
        UnitType: EAllocationUnitType.NodeHours,
        MappingOneToOne: true,
        UseAccountingStringForScheduler: false,
        Usernames: new List<string>() { UsernameValue },
        LicenseUrl: null,
        BypassHeappe: false,
        Clusters: [new Cluster("cluster-1", "/scratch", "/project")],
        AssignedLocations: [.. assignments.Length > 0 ? assignments : [Assignment()]]);

    /// <summary>A provider aggregate carrying exactly the named locations, with no allocations.</summary>
    internal static ProviderAggregate ProviderWith(params (string Name, string Endpoint)[] locations)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(ProviderNameValue), countryId: 1);
        foreach (var (name, endpoint) in locations.Length > 0
            ? locations
            : [(LocationNameValue, LocationEndpoint)])
        {
            Assert.True(provider.AddLocation(LocationName.From(name), endpoint, ELocationType.HPC, isShared: true).IsSuccess);
        }

        provider.ClearDomainEvents();
        return provider;
    }

    /// <summary>Runs the endpoint over an in-memory provider and returns the step it submitted.</summary>
    internal static Task<CheckHeappeStep> InitialStepAsync(ProviderAggregate provider, Request? request = null)
    {
        var repository = new Mock<IProviderRepository>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProviderAggregate?>(provider));

        return InitialStepAsync(repository.Object, request);
    }

    /// <summary>
    /// Runs the endpoint over whatever <see cref="IProviderRepository"/> is supplied — an in-memory double or
    /// the real EF-backed one — and returns the step it submitted.
    /// </summary>
    internal static async Task<CheckHeappeStep> InitialStepAsync(IProviderRepository providerRepository, Request? request = null)
    {
        var resources = new Mock<IResourceQuery>();
        resources.Setup(q => q.IsResourceNameTakenAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var userDirectory = new Mock<IUserDirectory>();
        userDirectory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserId(Guid.NewGuid()));

        StepRequest? submitted = null;
        var requestApplicationService = new Mock<IRequestApplicationService>();
        requestApplicationService
            .Setup(s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<StepRequest, string?, string?, string?, CancellationToken>((step, _, _, _, _) => submitted = step)
            .ReturnsAsync(new SubmitResponse(Guid.CreateVersion7(), RequestStatus.Accepted));

        var result = await CreateResourceOnLocationEndpoint.HandleAsync(
            ProviderNameValue,
            request ?? RequestFor(),
            OwningCaller(),
            providerRepository,
            resources.Object,
            Options.Create(new WellKnownRoles()),
            requestApplicationService.Object,
            userDirectory.Object,
            new RecordingAuditLogger<CreateResourceOnLocationEndpoint.AuditCategory>(),
            NullLogger<CreateResourceOnLocationEndpoint.AuditCategory>.Instance,
            TestContext.Current.CancellationToken);

        // A non-202 here means the fixture, not the subject, is wrong — say so before the cast fails.
        Assert.True(
            result is Microsoft.AspNetCore.Http.HttpResults.AcceptedAtRoute<Response>,
            $"The endpoint did not accept the fixture request; it returned {result.GetType().Name}.");

        return Assert.IsType<CheckHeappeStep>(submitted);
    }

    /// <summary>
    /// Serialise → deserialise → serialise, with the engine's own calls, and compare the two documents.
    /// <para>
    /// Record equality is deliberately NOT used: these records carry <see cref="List{T}"/> members, whose
    /// synthesised <c>Equals</c> is reference equality, so <c>Assert.Equal(step, replayed)</c> would fail for
    /// every conceivable shape and prove nothing. Comparing the serialised documents is the property that
    /// actually matters — the engine replays from the JSON, not from the instance.
    /// </para>
    /// </summary>
    internal static void AssertEngineRoundTrip(StepRequest step)
    {
        var type = step.GetType();

        // RequestWorker.cs:121 (and SubmitHandler.cs:85 for the initial step).
        using var written = System.Text.Json.JsonSerializer.SerializeToDocument(step, type);
        var raw = written.RootElement.GetRawText();

        // RequestWorkerStartupRecovery.cs:101 / RetryScheduledScanner.cs:114.
        var replayed = System.Text.Json.JsonSerializer.Deserialize(raw, type) as StepRequest;

        Assert.NotNull(replayed);
        Assert.Equal(step.RequestId, replayed.RequestId);
        Assert.Equal(step.StateName, replayed.StateName);

        using var rewritten = System.Text.Json.JsonSerializer.SerializeToDocument(replayed, type);
        Assert.Equal(raw, rewritten.RootElement.GetRawText());
    }
}
