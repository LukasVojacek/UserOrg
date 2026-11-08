using Core.Features.Resource.CheckAccountAccess;
using Core.Features.Resource.ReprovisionAccounts;
using Core.UnitTests.Shared;

using Heappe.Contracts;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using WorkflowEngine.Contracts.Ports;

using WorkflowRequestRepository = WorkflowEngine.Core.Ports.IRequestRepository;

namespace Core.UnitTests.Resources;

/// <summary>
/// The account endpoints of a pending create-resource workflow answer 404 for a provider that does not
/// exist, instead of throwing out of the handler as a 500.
/// </summary>
public sealed class ResourceAccountEndpointsProviderNotFoundTests
{
    private const string Provider = "acme";
    private const string ResName = "res-1";

    private static Caller ProviderCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Mock<IProviderRepository> MissingProvider()
    {
        var providerRepo = new Mock<IProviderRepository>();
        providerRepo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Core.Domain.Provider.Provider?)null);
        return providerRepo;
    }

    private static void AssertProviderNotFound(IResult result, Mock<WorkflowRequestRepository> requests)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
        requests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return404_When_CheckAccountAccessTargetsAMissingProvider()
    {
        var requests = new Mock<WorkflowRequestRepository>();

        var result = await CheckAccountAccessEndpoint.HandleAsync(
            Provider, ResName, ProviderCaller(), requests.Object, MissingProvider().Object,
            new Mock<IRequestApplicationService>().Object, Options.Create(new WellKnownRoles()),
            new RecordingAuditLogger<CheckAccountAccessEndpoint.AuditCategory>(),
            NullLogger<CheckAccountAccessEndpoint.AuditCategory>.Instance,
            new Mock<IHeappeService>().Object, new Mock<IProjectCreationService>().Object, CancellationToken.None);

        AssertProviderNotFound(result, requests);
    }

    [Fact]
    public async Task Should_Return404_When_ReprovisionAccountsTargetsAMissingProvider()
    {
        var requests = new Mock<WorkflowRequestRepository>();

        var result = await ReprovisionAccountsEndpoint.HandleAsync(
            Provider, ResName, new ReprovisionAccountsEndpoint.ReprovisionAccountsRequest(["user"]), ProviderCaller(),
            requests.Object, MissingProvider().Object, new Mock<IRequestApplicationService>().Object,
            Options.Create(new WellKnownRoles()), new RecordingAuditLogger<ReprovisionAccountsEndpoint.AuditCategory>(),
            NullLogger<ReprovisionAccountsEndpoint.AuditCategory>.Instance, CancellationToken.None);

        AssertProviderNotFound(result, requests);
    }
}
