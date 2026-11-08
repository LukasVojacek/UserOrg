using Core.DataAccess.Queries.Contracts;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Steps;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ELocationType = Core.Domain.Shared.ELocationType;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Resources;

/// <summary>
/// Characterization tests for <see cref="CreateResourceOnLocationEndpoint.HandleAsync"/>, written before the T-197-07
/// extract-method refactor. They pin every validation guard with its code and detail, the order of the guards
/// relative to each other and to the provider-write authorization, the soft-deleted location miss, and the resolved
/// location refs the submitted <see cref="CheckHeappeStep"/> carries, including how the HEAppE URL is picked.
/// </summary>
public sealed class CreateResourceOnLocationEndpointCharacterizationTests
{
    private const string Provider = "acme";
    private const string Location = "gpu";
    private const string ResName = "res-1";
    private const string HeappeUrl = "https://heappe.gpu";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FlowId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<IProviderRepository> _providers = new();
    private readonly Mock<IResourceQuery> _resources = new();
    private readonly Mock<IRequestApplicationService> _requests = new();
    private StepRequest? _submitted;

    public CreateResourceOnLocationEndpointCharacterizationTests()
    {
        _providers.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ProviderAggregate?>(ProviderWithLocation()));
        _resources.Setup(q => q.IsResourceNameTakenAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _requests.Setup(s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<StepRequest, string?, string?, string?, CancellationToken>((step, _, _, _, _) => _submitted = step)
            .ReturnsAsync(new SubmitResponse(FlowId, RequestStatus.Accepted));
    }

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Caller OtherProviderCaller() => new(
        new IdpId("svc-other"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    private static ProviderAggregate ProviderWithLocation(bool deleted = false)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: true);
        if (deleted)
            provider.RemoveLocation(LocationName.From(Location));
        provider.ClearDomainEvents();
        return provider;
    }

    private static Mock<IUserDirectory> UserDirectory()
    {
        var userDirectory = new Mock<IUserDirectory>();
        userDirectory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserId(Guid.NewGuid()));

        return userDirectory;
    }


    private static CreateResourceOnLocationEndpoint.LocationAssignment Assignment(
        string locationName = Location,
        string? aggregationName = "agg",
        IReadOnlyList<CreateResourceOnLocationEndpoint.SpecificationItem>? specifications = null) => new(
        locationName, EAllocationUnitType.NodeHours, aggregationName, AllocationUnitCount: 100,
        AllocationDescription: "alloc", PreferredAuthenticationType: "ssh",
        Specifications: specifications ?? [new("cpu", "16"), new(ApplicationConstants.HeappeUriKey, HeappeUrl)]);

    private static CreateResourceOnLocationEndpoint.Request ValidRequest() => new(
        ResName, "desc", "pi@example.com", Start, End, AccountString: "acct", UnitType: EAllocationUnitType.NodeHours,
        MappingOneToOne: true, UseAccountingStringForScheduler: true, BypassHeappe: false,
        Clusters: [new("cluster", "/scratch", "/project")],
        Usernames: new List<string>() { "user" }, LicenseUrl: null,
        AssignedLocations: [Assignment()]);

    private Task<IResult> InvokeAsync(CreateResourceOnLocationEndpoint.Request request, Caller? caller = null, string providerName = Provider) =>
        CreateResourceOnLocationEndpoint.HandleAsync(
            providerName, request, caller ?? OwnerCaller(),
            _providers.Object, _resources.Object,
            Options.Create(new WellKnownRoles()), _requests.Object, UserDirectory().Object,
            new RecordingAuditLogger<CreateResourceOnLocationEndpoint.AuditCategory>(), NullLogger<CreateResourceOnLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static ProblemHttpResult AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        return problem;
    }

    private void AssertNothingResolvedOrSubmitted()
    {
        _providers.Verify(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(_submitted);
    }

    private static readonly Dictionary<string, (CreateResourceOnLocationEndpoint.Request Request, string Code, string Detail)> EarlyGuards = new()
    {
        ["assignments null"] = (ValidRequest() with { AssignedLocations = null! }, "resource.locations_required", "At least one location assignment is required."),
        ["clusters null"] = (ValidRequest() with { Clusters = null! }, "resource.clusters_required", "At least one cluster is required."),
        ["assignments and clusters empty"] = (ValidRequest() with { AssignedLocations = [], Clusters = [] }, "resource.locations_required", "At least one location assignment is required."),
        ["assignment location blank"] = (ValidRequest() with { AssignedLocations = [Assignment("  ")] }, ProviderErrorCodes.InvalidLocationName, "LocationName cannot be empty"),
        ["assignment location too long"] = (ValidRequest() with { AssignedLocations = [Assignment(new string('l', 101))] }, ProviderErrorCodes.InvalidLocationName, "LocationName must not exceed 100 characters"),
        ["blank location before duplicate"] = (ValidRequest() with { AssignedLocations = [Assignment(), Assignment(), Assignment("")] }, ProviderErrorCodes.InvalidLocationName, "LocationName cannot be empty"),
        ["duplicate differing only by case"] = (ValidRequest() with { AssignedLocations = [Assignment("GPU"), Assignment(" gpu ")] }, "resource.duplicate_location_assignment", "Location 'GPU' is assigned twice with the same aggregation."),
        ["duplicate with null aggregation"] = (ValidRequest() with { AssignedLocations = [Assignment(aggregationName: null), Assignment(aggregationName: null)] }, "resource.duplicate_location_assignment", "Location 'gpu' is assigned twice with the same aggregation."),
        ["description 151"] = (ValidRequest() with { Description = new string('d', 151) }, CommonErrorCodes.InvalidFormat, "Description must not exceed 150 characters."),
        ["email 101"] = (ValidRequest() with { PrimaryInvestigatorEmail = new string('a', 89) + "@example.com" }, CommonErrorCodes.InvalidFormat, "A valid primary investigator email (max 100 characters) is required."),
        ["end equals start"] = (ValidRequest() with { EndDate = Start }, CommonErrorCodes.InvalidFormat, "EndDate must be after StartDate."),
        ["email before date"] = (ValidRequest() with { PrimaryInvestigatorEmail = " ", EndDate = Start }, CommonErrorCodes.InvalidFormat, "A valid primary investigator email (max 100 characters) is required."),
        ["date before assignments"] = (ValidRequest() with { EndDate = Start, AssignedLocations = [] }, CommonErrorCodes.InvalidFormat, "EndDate must be after StartDate."),
    };

    private static readonly Dictionary<string, (CreateResourceOnLocationEndpoint.Request Request, string Code, string Detail)> LateGuards = new()
    {
        ["unit type none"] = (ValidRequest() with { UnitType = EAllocationUnitType.None }, CommonErrorCodes.InvalidFormat, "AllocationUnitType is required."),
        ["cluster name blank"] = (ValidRequest() with { Clusters = [new(" ", "/scratch", "/project")] }, ProviderErrorCodes.InvalidLocationName, "LocationName cannot be empty"),
        ["scratch path blank"] = (ValidRequest() with { Clusters = [new("cluster", " ", "/project")] }, CommonErrorCodes.InvalidFormat, "ScratchPath is required."),
        ["project path blank"] = (ValidRequest() with { Clusters = [new("cluster", "/scratch", "")] }, CommonErrorCodes.InvalidFormat, "ProjectPath is required."),
        ["second cluster invalid"] = (ValidRequest() with { Clusters = [new("cluster", "/scratch", "/project"), new("cluster-2", "/scratch", null!)] }, CommonErrorCodes.InvalidFormat, "ProjectPath is required."),
        ["usernames missing"] = (ValidRequest() with { Usernames = null! }, CommonErrorCodes.InvalidFormat, "Usernames are required."),
        ["usernames empty"] = (ValidRequest() with { Usernames = [] }, CommonErrorCodes.InvalidFormat, "Usernames are required."),
        ["usernames blank"] = (ValidRequest() with { Usernames = new List<string>() { " " } }, CommonErrorCodes.InvalidFormat, "Username is required and must be all lowercase."),
        ["unit type before cluster"] = (ValidRequest() with { UnitType = EAllocationUnitType.None, Clusters = [new("", "", "")] }, CommonErrorCodes.InvalidFormat, "AllocationUnitType is required."),
        ["cluster name before paths"] = (ValidRequest() with { Clusters = [new("", "", "")] }, ProviderErrorCodes.InvalidLocationName, "LocationName cannot be empty"),
        ["scratch before project path"] = (ValidRequest() with { Clusters = [new("cluster", "", "")] }, CommonErrorCodes.InvalidFormat, "ScratchPath is required."),
        ["cluster before username"] = (ValidRequest() with { Clusters = [new("cluster", "", "/project")], Usernames = new List<string>() { "" } }, CommonErrorCodes.InvalidFormat, "ScratchPath is required."),
    };

    public static TheoryData<string> EarlyGuardScenarios() => new(EarlyGuards.Keys);

    public static TheoryData<string> LateGuardScenarios() => new(LateGuards.Keys);

    [Theory]
    [MemberData(nameof(EarlyGuardScenarios))]
    public async Task Should_RejectWith400BeforeAuthorization_When_TheBodyFailsAnEarlyGuard(string scenario)
    {
        var (request, code, detail) = EarlyGuards[scenario];

        var result = await InvokeAsync(request, OtherProviderCaller());

        var problem = AssertProblem(result, StatusCodes.Status400BadRequest, code);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
        AssertNothingResolvedOrSubmitted();
    }

    [Fact]
    public async Task Should_RejectAMalformedProviderNameWith400BeforeTheBody()
    {
        var result = await InvokeAsync(ValidRequest() with { Name = "" }, OtherProviderCaller(), providerName: "  ");

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidName);
        AssertNothingResolvedOrSubmitted();
    }

    [Theory]
    [MemberData(nameof(LateGuardScenarios))]
    public async Task Should_RejectWith400AfterAuthorizationAndBeforeTheProviderLookup_When_TheBodyFailsALateGuard(string scenario)
    {
        var (request, code, detail) = LateGuards[scenario];

        var result = await InvokeAsync(request);

        var problem = AssertProblem(result, StatusCodes.Status400BadRequest, code);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
        AssertNothingResolvedOrSubmitted();
    }

    [Theory]
    [MemberData(nameof(LateGuardScenarios))]
    public async Task Should_Return403_When_ANonOwnerSendsABodyThatOnlyFailsALateGuard(string scenario)
    {
        var result = await InvokeAsync(LateGuards[scenario].Request, OtherProviderCaller());

        var problem = AssertProblem(result, StatusCodes.Status403Forbidden, "provider.write_forbidden");
        Assert.Equal("This identity may not write to this provider.", problem.ProblemDetails.Detail);
        AssertNothingResolvedOrSubmitted();
    }

    [Fact]
    public async Task Should_Accept_When_DescriptionIsNullOrExactly150Characters()
    {
        Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(await InvokeAsync(ValidRequest() with { Description = null }));
        Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(await InvokeAsync(ValidRequest() with { Description = new string('d', 150) }));
    }

    [Fact]
    public async Task Should_ReturnLocationNotFoundAndNotSubmit_When_TheNamedLocationIsSoftDeleted()
    {
        _providers.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<ProviderAggregate?>(ProviderWithLocation(deleted: true)));

        var result = await InvokeAsync(ValidRequest());

        var problem = AssertProblem(result, StatusCodes.Status404NotFound, ProviderErrorCodes.LocationNotFound);
        Assert.Equal("Location 'gpu' not found for provider 'acme'.", problem.ProblemDetails.Detail);
        Assert.Null(_submitted);
    }

    [Fact]
    public async Task Should_SubmitTheRequestItselfWithTheStoredLocationNameEndpointAndHeappeUrl()
    {
        var request = ValidRequest() with { AssignedLocations = [Assignment("GPU")] };

        await InvokeAsync(request);

        var step = Assert.IsType<CheckHeappeStep>(_submitted);
        Assert.Same(request, step.Data);
        Assert.Equal(new ResolvedLocationRef("gpu", Domain.Shared.ELocationType.HPC, "https://gpu", HeappeUrl), Assert.Single(step.ResolvedLocations));
    }

    [Fact]
    public async Task Should_ResolveANullHeappeUrl_When_SpecificationsAreNullOrLackTheHeappeUriKey()
    {
        var request = ValidRequest() with
        {
            AssignedLocations = [Assignment(aggregationName: "a", specifications: [new("cpu", "16")])],
        };
        await InvokeAsync(request);
        Assert.Null(Assert.Single(Assert.IsType<CheckHeappeStep>(_submitted).ResolvedLocations).heappeUrl);

        _submitted = null;
        await InvokeAsync(ValidRequest() with { AssignedLocations = [Assignment() with { Specifications = null }] });
        Assert.Null(Assert.Single(Assert.IsType<CheckHeappeStep>(_submitted).ResolvedLocations).heappeUrl);
    }

    [Fact]
    public async Task Should_GiveEveryRefOfALocationTheHeappeUrlOfItsFirstAssignment_When_TheLocationIsAssignedTwice()
    {
        var request = ValidRequest() with
        {
            AssignedLocations =
            [
                Assignment(aggregationName: "a", specifications: [new(ApplicationConstants.HeappeUriKey, "https://first")]),
                Assignment(aggregationName: "b", specifications: [new(ApplicationConstants.HeappeUriKey, "https://second")]),
            ],
        };

        await InvokeAsync(request);

        var refs = Assert.IsType<CheckHeappeStep>(_submitted).ResolvedLocations;
        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal(new ResolvedLocationRef("gpu", Domain.Shared.ELocationType.HPC, "https://gpu", "https://first"), r));
    }

    [Fact]
    public async Task Should_ResolveTheLocationButNotItsHeappeUrl_When_TheAssignedNameCarriesSurroundingWhitespace()
    {
        await InvokeAsync(ValidRequest() with { AssignedLocations = [Assignment(" gpu ")] });

        Assert.Equal(new ResolvedLocationRef("gpu", Domain.Shared.ELocationType.HPC, "https://gpu", null), Assert.Single(Assert.IsType<CheckHeappeStep>(_submitted).ResolvedLocations));
    }
}
