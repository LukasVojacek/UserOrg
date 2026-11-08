using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Resource.CreateResourceOnLocation;
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
/// Focused tests for the async-submit contract of the create-and-allocate endpoint (ADR-0057 / B05):
/// a valid submit returns <c>202 Accepted</c> whose <c>Location</c> targets the status route
/// (<c>GetResourceRequestStatusV2</c>) by name, carrying the flow <c>RequestId</c>. The broader
/// endpoint test rewrite is owned by B12.
/// </summary>
public sealed class CreateResourceOnLocation202ContractTests
{
    private const string Provider = "acme";
    private const string Location = "gpu";
    private const string ResName = "res-1";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FlowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Caller ProviderCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static ProviderAggregate ProviderWithLocation()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: true);
        provider.ClearDomainEvents();
        return provider;
    }

    private static Mock<IProviderRepository> ProviderRepo(ProviderAggregate provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProviderAggregate?>(provider));
        return repo;
    }

    private static Mock<IResourceQuery> ResourceQuery()
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.IsResourceNameTakenAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return query;
    }

    private static Mock<IRequestApplicationService> RequestAppService() =>
        SubmittingService(RequestStatus.Accepted);

    private static Mock<IRequestApplicationService> SubmittingService(RequestStatus status)
    {
        var svc = new Mock<IRequestApplicationService>();
        svc.Setup(s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitResponse(FlowId, status));
        return svc;
    }

    private static Mock<IUserDirectory> UserDirectory()
    {
        var userDirectory = new Mock<IUserDirectory>();
        userDirectory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserId(Guid.NewGuid()));

        return userDirectory;
    }

    private static CreateResourceOnLocationEndpoint.LocationAssignment LocationAssignment() => new(
        Location, EAllocationUnitType.NodeHours, AllocationUnitCount: 100,
        AllocationDescription: "alloc", AggregationName: "agg",
        PreferredAuthenticationType: "ssh",
        Specifications: [new("cpu", "16")]);

    private static CreateResourceOnLocationEndpoint.Request ValidRequest() => new(
        ResName, "desc", "pi@example.com", Start, End, AccountString: "acct",
        UnitType: EAllocationUnitType.NodeHours, MappingOneToOne: true, UseAccountingStringForScheduler: true, BypassHeappe: false,
        Usernames: new List<string>() { "user" }, LicenseUrl: null, Clusters: [new("cluster", "/scratch", "/project")],
        AssignedLocations: [LocationAssignment()]);

    private static Task<IResult> InvokeAsync(Mock<IRequestApplicationService> service) =>
        CreateResourceOnLocationEndpoint.HandleAsync(
            Provider, ValidRequest(), ProviderCaller(),
            ProviderRepo(ProviderWithLocation()).Object, ResourceQuery().Object,
            Options.Create(new WellKnownRoles()), service.Object, UserDirectory().Object,
            new RecordingAuditLogger<CreateResourceOnLocationEndpoint.AuditCategory>(),
            NullLogger<CreateResourceOnLocationEndpoint.AuditCategory>.Instance,
            CancellationToken.None);

    [Fact]
    public async Task Should_ReturnAcceptedWithStatusRouteLocation_When_SubmitAccepted()
    {
        var result = await InvokeAsync(RequestAppService());

        var accepted = Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.Equal("GetResourceRequestStatusV2", accepted.RouteName);
        Assert.True(accepted.RouteValues!.TryGetValue("requestId", out var routeRequestId));
        Assert.Equal(FlowId, routeRequestId);
        Assert.Equal(ResName, accepted.Value!.Name);
        Assert.Equal(FlowId, accepted.Value.RequestId);
    }

    [Fact]
    public async Task Should_StillReturnAccepted_When_SubmitReportsFailedSynchronously()
    {
        // SubmitAsync never yields Failed synchronously; the old dead Failed branch is gone, so even a
        // (theoretical) non-Accepted synchronous status still produces the 202 accepted contract.
        var result = await InvokeAsync(SubmittingService(RequestStatus.Failed));

        Assert.IsType<AcceptedAtRoute<CreateResourceOnLocationEndpoint.Response>>(result);
    }
}
