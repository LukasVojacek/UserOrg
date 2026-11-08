using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Steps;
using Core.UnitTests.Shared;

using Heappe.Contracts;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ELocationType = Core.Domain.Shared.ELocationType;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Resources;

/// <summary>
/// Handler tests for the async create-and-allocate endpoint (ADR-0043, ADR-0050, ADR-0057). The endpoint no
/// longer persists a resource synchronously: after validation, provider-write authorization, owning-provider
/// resolution (404), per-provider name pre-check (409) and per-location existence (404) it hands the work to the
/// workflow via <see cref="IRequestApplicationService.SubmitAsync"/> and returns <c>202 Accepted</c> pointing at
/// the status route — no resource, no allocation, no <c>SaveChanges</c> at request time (the HEAppE liveness gate,
/// allocation-unit existence and non-shared invariant now live inside the workflow, not this endpoint). The exact
/// 202 Location/route-name contract is pinned by <c>CreateResourceOnLocation202ContractTests</c>; this suite pins
/// the pre-submit guards and the initial step (a <see cref="CheckHeappeStep"/> carrying the provider, the request
/// data and every resolved location) that SubmitAsync receives.
/// </summary>
public sealed class CreateResourceOnLocationEndpointTests
{
    private const string Provider = "acme";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";
    private const string Location = "gpu";
    private const string OtherLocation = "cpu";
    private const string ResName = "res-1";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FlowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Caller ProviderCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    // A service key on purpose, owning a DIFFERENT provider: the 403 has to come from the ownership
    // comparison. An interactive caller here would be denied by D9's identity-kind gate before ownership
    // is ever compared, and the test would pass against a build that had lost the ownership check.
    private static Caller OtherProviderCaller() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    private static ProviderAggregate ProviderWithLocation()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: true);
        provider.ClearDomainEvents();
        return provider;
    }

    private static ProviderAggregate ProviderWithTwoLocations()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: true);
        provider.AddLocation(LocationName.From(OtherLocation), "https://cpu", ELocationType.HPC, isShared: true);
        provider.ClearDomainEvents();
        return provider;
    }

    private static Mock<IProviderRepository> ProviderRepo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<IResourceQuery> ResourceQuery(bool nameTaken)
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.IsResourceNameTakenAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nameTaken);
        return query;
    }

    private static Mock<IRequestApplicationService> RequestAppService()
    {
        var svc = new Mock<IRequestApplicationService>();
        svc.Setup(s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitResponse(FlowId, RequestStatus.Accepted));
        return svc;
    }

    // Same stub as RequestAppService, but also captures the StepRequest handed to SubmitAsync so tests can assert
    // on the step the workflow was entered with.
    private static (Mock<IRequestApplicationService> Mock, Func<StepRequest?> Submitted) CapturingRequestAppService()
    {
        StepRequest? submitted = null;
        var svc = RequestAppService();
        svc.Setup(s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<StepRequest, string?, string?, string?, CancellationToken>((step, _, _, _, _) => submitted = step)
            .ReturnsAsync(new SubmitResponse(FlowId, RequestStatus.Accepted));
        return (svc, () => submitted);
    }

    private static CreateResourceOnLocationEndpoint.LocationAssignment LocationAssignment(
        string locationName = Location, string? aggregationName = "agg") => new(
        locationName, EAllocationUnitType.NodeHours, AllocationUnitCount: 100,
        AllocationDescription: "alloc", AggregationName: aggregationName,
        PreferredAuthenticationType: "ssh",
        Specifications: [new("cpu", "16")]);

    private static Mock<IUserDirectory> UserDirectory()
    {
        var userDirectory = new Mock<IUserDirectory>();
        userDirectory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserId(Guid.NewGuid()));

        return userDirectory;
    }

    private static CreateResourceOnLocationEndpoint.Request ValidRequest() => new(
        ResName, "desc", "pi@example.com", Start, End, AccountString: "acct", UnitType: EAllocationUnitType.NodeHours, MappingOneToOne: true, UseAccountingStringForScheduler: true, Usernames: new List<string>() { "user" }, LicenseUrl: null, Clusters: [new("cluster", "/scratch", "/project")], BypassHeappe: false,
        AssignedLocations: [LocationAssignment()]);

    private static Task<IResult> InvokeAsync(
        Caller caller, ProviderAggregate? provider, bool nameTaken,
        CreateResourceOnLocationEndpoint.Request? request = null,
        Mock<IRequestApplicationService>? requestAppService = null) =>
        CreateResourceOnLocationEndpoint.HandleAsync(
            Provider, request ?? ValidRequest(), caller,
            ProviderRepo(provider).Object, ResourceQuery(nameTaken).Object,
            Options.Create(new WellKnownRoles()), (requestAppService ?? RequestAppService()).Object, UserDirectory().Object,
            new RecordingAuditLogger<CreateResourceOnLocationEndpoint.AuditCategory>(),
            NullLogger<CreateResourceOnLocationEndpoint.AuditCategory>.Instance,
            CancellationToken.None);

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;


    [Fact]
    public async Task Should_SubmitCheckHeappeAndReturnAccepted_When_Valid()
    {
        var provider = ProviderWithLocation();
        var (requestAppService, submitted) = CapturingRequestAppService();

        var result = await InvokeAsync(ProviderCaller(), provider, nameTaken: false, requestAppService: requestAppService);

        // Async submit (ADR-0057): 202 to the status route, no synchronous persistence.
        var accepted = Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.Equal("GetResourceRequestStatusV2", accepted.RouteName);
        Assert.Equal(FlowId, accepted.RouteValues!["requestId"]);
        Assert.Equal(ResName, accepted.Value!.Name);
        Assert.Equal(FlowId, accepted.Value.RequestId);

        // The workflow is entered at CheckHeappe, carrying the provider's NATURAL KEY (T-055-04 — never the
        // loaded aggregate), the raw request and the resolved location ref(s) — nothing is persisted at the
        // endpoint.
        requestAppService.Verify(
            s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var step = Assert.IsType<CheckHeappeStep>(submitted());
        Assert.Equal("CheckHeappe", step.StateName);
        Assert.Equal(ProviderName.From(Provider), step.ProviderName);
        Assert.Equal(ResName, step.Data.Name);
        Assert.Equal(Location, Assert.Single(step.ResolvedLocations).LocationName);
    }

    [Fact]
    public async Task Should_SubmitBothResolvedLocations_When_MultipleLocations()
    {
        var provider = ProviderWithTwoLocations();
        var (requestAppService, submitted) = CapturingRequestAppService();
        var request = ValidRequest() with
        {
            AssignedLocations = [LocationAssignment(Location), LocationAssignment(OtherLocation, aggregationName: "other-agg")],
        };

        var result = await InvokeAsync(ProviderCaller(), provider, nameTaken: false, request, requestAppService);

        Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(result);
        var step = Assert.IsType<CheckHeappeStep>(submitted());
        Assert.Equal(2, step.ResolvedLocations.Count);
        Assert.Contains(step.ResolvedLocations, l => l.LocationName == Location);
        Assert.Contains(step.ResolvedLocations, l => l.LocationName == OtherLocation);
    }

    [Fact]
    public async Task Should_Accept_When_SameLocationDifferentAggregationTwice()
    {
        // Same location, distinct aggregations — two legitimate allocations (ADR-0050 §3): both are resolved and
        // handed to the workflow.
        var provider = ProviderWithLocation();
        var (requestAppService, submitted) = CapturingRequestAppService();
        var request = ValidRequest() with
        {
            AssignedLocations = [LocationAssignment(Location, aggregationName: "agg-a"), LocationAssignment(Location, aggregationName: "agg-b")],
        };

        var result = await InvokeAsync(ProviderCaller(), provider, nameTaken: false, request, requestAppService);

        Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(result);
        var step = Assert.IsType<CheckHeappeStep>(submitted());
        Assert.Equal(2, step.ResolvedLocations.Count);
    }

    [Fact]
    public async Task Should_ReturnConflictAndNotSubmit_When_NameTakenUnderProvider()
    {
        var requestAppService = RequestAppService();

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: true, requestAppService: requestAppService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("resource.name_already_exists", problem.ProblemDetails.Extensions["code"]);
        requestAppService.Verify(
            s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnConflictWithFullDetailAndCheckPerProviderName_When_NameAlreadyExistsForProvider()
    {
        // Pins today's bare pre-check contract: IsResourceNameTakenAsync is a plain per-provider-and-name boolean.
        // The endpoint does not distinguish between resource states or consult any availability concept — any name
        // collision under the caller's provider unconditionally 409s and never reaches SubmitAsync.
        var query = ResourceQuery(nameTaken: true);
        var requestAppService = RequestAppService();

        var result = await CreateResourceOnLocationEndpoint.HandleAsync(
            Provider, ValidRequest(), ProviderCaller(),
            ProviderRepo(ProviderWithLocation()).Object, query.Object, Options.Create(new WellKnownRoles()),
            requestAppService.Object, UserDirectory().Object,
            new RecordingAuditLogger<CreateResourceOnLocationEndpoint.AuditCategory>(),
            NullLogger<CreateResourceOnLocationEndpoint.AuditCategory>.Instance,
            CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("resource.name_already_exists", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal($"A resource named '{ResName}' already exists for this provider.", problem.ProblemDetails.Detail);

        // The pre-check is invoked with exactly the caller's provider and the requested resource name — the sole
        // inputs the current bare-bool contract accepts.
        query.Verify(
            q => q.IsResourceNameTakenAsync(ProviderName.From(Provider), ResourceName.From(ResName), It.IsAny<CancellationToken>()),
            Times.Once);
        requestAppService.Verify(
            s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProviderMissing()
    {
        var result = await InvokeAsync(ProviderCaller(), provider: null, nameTaken: false);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFoundNamingLocationAndNotSubmit_When_LocationUnknown()
    {
        // A provider WITHOUT the requested location → 404 naming the specific missing location, before the
        // workflow is ever entered.
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        var requestAppService = RequestAppService();

        var result = await InvokeAsync(ProviderCaller(), provider, nameTaken: false, requestAppService: requestAppService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.LocationNotFound, problem.ProblemDetails.Extensions["code"]);
        Assert.Contains(Location, problem.ProblemDetails.Detail);
        requestAppService.Verify(
            s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnForbiddenAndNotResolveProvider_When_NotAuthorized()
    {
        var providerRepo = ProviderRepo(ProviderWithLocation());
        var requestAppService = RequestAppService();

        var result = await CreateResourceOnLocationEndpoint.HandleAsync(
            Provider, ValidRequest(), OtherProviderCaller(),
            providerRepo.Object, ResourceQuery(nameTaken: false).Object, Options.Create(new WellKnownRoles()),
            requestAppService.Object, UserDirectory().Object,
            new RecordingAuditLogger<CreateResourceOnLocationEndpoint.AuditCategory>(),
            NullLogger<CreateResourceOnLocationEndpoint.AuditCategory>.Instance,
            CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
        // Authorization precedes the provider load and the submit.
        providerRepo.Verify(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()), Times.Never);
        requestAppService.Verify(
            s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidation_When_NameInvalid()
    {
        var request = ValidRequest() with { Name = "" };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("resource.invalid_name", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidation_When_DescriptionTooLong()
    {
        var request = ValidRequest() with { Description = new string('x', 151) };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidation_When_PrimaryInvestigatorEmailMissing()
    {
        var request = ValidRequest() with { PrimaryInvestigatorEmail = "" };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidation_When_PrimaryInvestigatorEmailInvalid()
    {
        var request = ValidRequest() with { PrimaryInvestigatorEmail = "not-an-email" };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidation_When_EndDateNotAfterStartDate()
    {
        var request = ValidRequest() with { StartDate = End, EndDate = Start };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidation_When_ClustersEmpty()
    {
        // B06: Clusters is a distinct input validated with its own code.
        var request = ValidRequest() with { Clusters = [] };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("resource.clusters_required", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationAssignmentRequired_When_AssignedLocationsEmpty()
    {
        // B06 / D1: Clusters present but AssignedLocations empty — the actual allocations the workflow needs are
        // missing, so the "at least one assignment" rule must fire with its own code.
        var request = ValidRequest() with { AssignedLocations = [] };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("resource.locations_required", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidation_When_SameLocationAndAggregationTwice()
    {
        var request = ValidRequest() with
        {
            AssignedLocations = [LocationAssignment(Location, aggregationName: "agg"), LocationAssignment(Location, aggregationName: "agg")],
        };

        var result = await InvokeAsync(ProviderCaller(), ProviderWithLocation(), nameTaken: false, request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("resource.duplicate_location_assignment", problem.ProblemDetails.Extensions["code"]);
    }
}
